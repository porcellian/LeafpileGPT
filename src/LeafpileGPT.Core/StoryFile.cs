using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LeafpileGPT.Core;

/// <summary>A validated, immutable Z-machine story image and its parsed header.</summary>
public sealed class StoryFile
{
    public const int HeaderLength = 64;

    private const int ChecksumOffset = 0x1C;
    private const int FileLengthOffset = 0x1A;
    private const int GlobalsTableLength = 240 * sizeof(ushort);

    private readonly byte[] _bytes;

    private StoryFile(byte[] bytes, StoryHeader header, long sourceFileSizeBytes, string sourceFileSha256, string loadedStorySha256)
    {
        _bytes = bytes;
        Header = header;
        SourceFileSizeBytes = sourceFileSizeBytes;
        SourceFileSha256 = sourceFileSha256;
        LoadedStorySha256 = loadedStorySha256;
    }

    public StoryHeader Header { get; }

    /// <summary>Gets the validated story bytes, without any trailing file padding.</summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    public int Length => _bytes.Length;

    /// <summary>Gets the physical size of the input file or parsed image before story padding is trimmed.</summary>
    public long SourceFileSizeBytes { get; }

    /// <summary>Gets the SHA-256 fingerprint of the exact bytes supplied by the source.</summary>
    public string SourceFileSha256 { get; }

    /// <summary>Gets the SHA-256 fingerprint of the effective story image after declared padding is removed.</summary>
    public string LoadedStorySha256 { get; }

    public static StoryFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < HeaderLength)
        {
            throw TruncatedHeader(stream.Length);
        }

        Span<byte> headerBytes = stackalloc byte[HeaderLength];
        stream.ReadExactly(headerBytes);

        var version = ReadVersion(headerBytes);
        var expectedLength = ResolveFileLength(headerBytes, version, stream.Length);
        ValidateFileLength(expectedLength, version);

        if (expectedLength > stream.Length)
        {
            throw new StoryFileFormatException(
                $"Story header declares {expectedLength} bytes, but the file contains only {stream.Length} bytes.");
        }

        var storyBytes = new byte[expectedLength];
        headerBytes.CopyTo(storyBytes);
        stream.Position = HeaderLength;
        stream.ReadExactly(storyBytes.AsSpan(HeaderLength));

        stream.Position = 0;
        var sourceHash = Convert.ToHexString(SHA256.HashData(stream));
        return Parse(storyBytes, stream.Length, sourceHash);
    }

    public static StoryFile Parse(ReadOnlySpan<byte> data)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(data));
        return Parse(data, data.Length, sourceHash);
    }

    private static StoryFile Parse(ReadOnlySpan<byte> data, long sourceFileSizeBytes, string sourceFileSha256)
    {
        if (data.Length < HeaderLength)
        {
            throw TruncatedHeader(data.Length);
        }

        var version = ReadVersion(data);
        var fileLength = ResolveFileLength(data, version, data.Length);
        ValidateFileLength(fileLength, version);

        if (fileLength > data.Length)
        {
            throw new StoryFileFormatException(
                $"Story header declares {fileLength} bytes, but the supplied data contains only {data.Length} bytes.");
        }

        var storyBytes = data[..fileLength].ToArray();
        var header = ParseHeader(storyBytes, version, fileLength);
        var loadedStoryHash = sourceFileSizeBytes == fileLength && data.Length == fileLength
            ? sourceFileSha256
            : Convert.ToHexString(SHA256.HashData(storyBytes));
        return new StoryFile(
            storyBytes,
            header,
            sourceFileSizeBytes,
            sourceFileSha256,
            loadedStoryHash);
    }

    private static StoryHeader ParseHeader(ReadOnlySpan<byte> data, byte version, int fileLength)
    {
        var highMemoryBase = ReadWord(data, 0x04);
        var initialProgramCounter = ReadWord(data, 0x06);
        var dictionaryAddress = ReadWord(data, 0x08);
        var objectTableAddress = ReadWord(data, 0x0A);
        var globalsTableAddress = ReadWord(data, 0x0C);
        var staticMemoryBase = ReadWord(data, 0x0E);
        var fileLengthWord = ReadWord(data, FileLengthOffset);
        var storedChecksumWord = ReadWord(data, ChecksumOffset);
        var routinesOffset = version >= 6 ? ReadWord(data, 0x28) : (ushort)0;

        ValidateMemoryMap(
            version,
            fileLength,
            highMemoryBase,
            initialProgramCounter,
            dictionaryAddress,
            objectTableAddress,
            globalsTableAddress,
            staticMemoryBase,
            routinesOffset);

        var checksumStatus = GetChecksumStatus(version, fileLengthWord, storedChecksumWord);
        ushort? storedChecksum = null;
        ushort? calculatedChecksum = null;

        if (checksumStatus != StoryChecksumStatus.NotDefined
            && checksumStatus != StoryChecksumStatus.UnavailableInLegacyVersion3)
        {
            var actualChecksum = CalculateChecksum(data);
            if (actualChecksum != storedChecksumWord)
            {
                throw new StoryFileChecksumException(storedChecksumWord, actualChecksum);
            }

            storedChecksum = storedChecksumWord;
            calculatedChecksum = actualChecksum;
        }

        return new StoryHeader(
            data[..HeaderLength],
            version,
            ReadWord(data, 0x02),
            highMemoryBase,
            initialProgramCounter,
            dictionaryAddress,
            objectTableAddress,
            globalsTableAddress,
            staticMemoryBase,
            Encoding.Latin1.GetString(data.Slice(0x12, 6)),
            version >= 3 ? fileLengthWord : null,
            fileLength,
            storedChecksum,
            calculatedChecksum,
            checksumStatus);
    }

    private static void ValidateMemoryMap(
        byte version,
        int fileLength,
        ushort highMemoryBase,
        ushort initialProgramCounter,
        ushort dictionaryAddress,
        ushort objectTableAddress,
        ushort globalsTableAddress,
        ushort staticMemoryBase,
        ushort routinesOffset)
    {
        if (staticMemoryBase < HeaderLength)
        {
            throw new StoryFileFormatException(
                $"Static memory base 0x{staticMemoryBase:X4} must leave the full 64-byte header in dynamic memory.");
        }

        if (staticMemoryBase > fileLength)
        {
            throw new StoryFileFormatException(
                $"Static memory base 0x{staticMemoryBase:X4} lies beyond the {fileLength}-byte story file.");
        }

        if (highMemoryBase < staticMemoryBase)
        {
            throw new StoryFileFormatException(
                $"High memory base 0x{highMemoryBase:X4} overlaps dynamic memory ending at 0x{staticMemoryBase - 1:X4}.");
        }

        if (highMemoryBase > fileLength)
        {
            throw new StoryFileFormatException(
                $"High memory base 0x{highMemoryBase:X4} lies beyond the {fileLength}-byte story file.");
        }

        if (version == 6)
        {
            var mainRoutineAddress = ZMachineAddressing.PackedToByteAddress(
                initialProgramCounter,
                version,
                PackedAddressKind.Routine,
                routinesOffset);
            if (mainRoutineAddress < HeaderLength || mainRoutineAddress >= fileLength)
            {
                throw new StoryFileFormatException(
                    $"V6 packed main-routine address 0x{initialProgramCounter:X4} resolves outside the story file.");
            }
        }
        else if (initialProgramCounter < HeaderLength || initialProgramCounter >= fileLength)
        {
            throw new StoryFileFormatException(
                $"Initial program counter 0x{initialProgramCounter:X4} lies outside executable story data.");
        }

        ValidateFileRange("Dictionary header", dictionaryAddress, 4, fileLength);
        ValidateDynamicRange(
            "Object table",
            objectTableAddress,
            version <= 3 ? 31 * sizeof(ushort) : 63 * sizeof(ushort),
            staticMemoryBase);
        ValidateDynamicRange("Global variables table", globalsTableAddress, GlobalsTableLength, staticMemoryBase);
    }

    private static void ValidateDynamicRange(string name, ushort address, int length, ushort staticMemoryBase)
    {
        var endAddress = (long)address + length;
        if (address < HeaderLength || endAddress > staticMemoryBase)
        {
            throw new StoryFileFormatException(
                $"{name} range 0x{address:X4}-0x{endAddress - 1:X4} is not contained in dynamic memory ending at 0x{staticMemoryBase - 1:X4}.");
        }
    }

    private static void ValidateFileRange(string name, ushort address, int length, int fileLength)
    {
        var endAddress = (long)address + length;
        if (address < HeaderLength || endAddress > fileLength)
        {
            throw new StoryFileFormatException(
                $"{name} range 0x{address:X4}-0x{endAddress - 1:X4} is outside the {fileLength}-byte story file.");
        }
    }

    private static StoryChecksumStatus GetChecksumStatus(byte version, ushort fileLengthWord, ushort checksumWord)
    {
        if (version < 3)
        {
            return StoryChecksumStatus.NotDefined;
        }

        if (version == 3 && fileLengthWord == 0 && checksumWord == 0)
        {
            return StoryChecksumStatus.UnavailableInLegacyVersion3;
        }

        return StoryChecksumStatus.Valid;
    }

    private static ushort CalculateChecksum(ReadOnlySpan<byte> data)
    {
        uint checksum = 0;
        for (var i = HeaderLength; i < data.Length; i++)
        {
            checksum = (checksum + data[i]) & ushort.MaxValue;
        }

        return (ushort)checksum;
    }

    private static int ResolveFileLength(ReadOnlySpan<byte> header, byte version, long physicalLength)
    {
        if (version < 3)
        {
            return CheckedLength(physicalLength);
        }

        var lengthWord = ReadWord(header, FileLengthOffset);
        if (lengthWord == 0)
        {
            if (version > 3)
            {
                throw new StoryFileFormatException($"Version {version} stories must declare their file length in header bytes 0x1A-0x1B.");
            }

            return CheckedLength(physicalLength);
        }

        var multiplier = version <= 3 ? 2 : version <= 5 ? 4 : 8;
        return lengthWord * multiplier;
    }

    private static int CheckedLength(long length)
    {
        if (length > int.MaxValue)
        {
            throw new StoryFileFormatException("Story file is too large to load.");
        }

        return (int)length;
    }

    private static void ValidateFileLength(int fileLength, byte version)
    {
        var maximumLength = version <= 3 ? 128 * 1024 : version <= 5 ? 256 * 1024 : 512 * 1024;
        if (fileLength < HeaderLength)
        {
            throw new StoryFileFormatException(
                $"Declared story length {fileLength} bytes is shorter than the {HeaderLength}-byte header.");
        }

        if (fileLength > maximumLength)
        {
            throw new StoryFileFormatException(
                $"Version {version} story length {fileLength} exceeds the {maximumLength}-byte limit.");
        }
    }

    private static byte ReadVersion(ReadOnlySpan<byte> data)
    {
        var version = data[0];
        if (version is < 1 or > 8)
        {
            throw new StoryFileFormatException($"Unsupported Z-machine version {version}; expected a version from 1 through 8.");
        }

        return version;
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, int address) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(address, sizeof(ushort)));

    private static StoryFileFormatException TruncatedHeader(long length) =>
        new($"Story file is truncated: the header requires {HeaderLength} bytes, but only {length} are available.");
}
