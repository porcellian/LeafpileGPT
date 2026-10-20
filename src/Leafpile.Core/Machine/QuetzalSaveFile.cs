using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Reads and writes the Quetzal IFZS save-file format.</summary>
public static class QuetzalSaveFile
{
    private static readonly byte[] FormId = "FORM"u8.ToArray();
    private static readonly byte[] IfzsId = "IFZS"u8.ToArray();

    public static byte[] Write(
        ZMachineState state,
        int resumeAddress,
        IReadOnlyList<string>? annotations = null,
        QuetzalInterpreterData? interpreterData = null,
        bool useCompressedMemory = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateProgramCounter(resumeAddress, state.Header.FileLength);
        var originalStory = state.GetOriginalStoryImage().Span;
        var dynamicMemory = state.Memory.GetDynamicMemory();
        var chunks = new List<(string Id, byte[] Data)>
        {
            ("IFhd", WriteIdentity(state, resumeAddress, originalStory)),
            (useCompressedMemory ? "CMem" : "UMem", useCompressedMemory
                ? CompressMemory(dynamicMemory, originalStory[..state.Memory.DynamicMemoryLength])
                : dynamicMemory),
            ("Stks", WriteStack(state.CallStack.CaptureSnapshot(), state.Header.Version))
        };

        if (annotations is not null)
        {
            foreach (var annotation in annotations)
            {
                ArgumentNullException.ThrowIfNull(annotation);
                if (annotation.Any(character => character is < ' ' or > '~'))
                {
                    throw new ArgumentException("Quetzal annotations may contain only printable ASCII characters.", nameof(annotations));
                }

                chunks.Add(("ANNO", Encoding.ASCII.GetBytes(annotation)));
            }
        }

        if (interpreterData is not null)
        {
            chunks.Add(("IntD", WriteInterpreterData(interpreterData)));
        }

        return WriteForm(chunks);
    }

    public static QuetzalSaveDocument Read(ReadOnlyMemory<byte> saveFile, LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);
        var input = saveFile.Span;
        if (input.Length < 12 || !input[..4].SequenceEqual(FormId) || !input.Slice(8, 4).SequenceEqual(IfzsId))
        {
            throw Invalid("The save file is not a FORM IFZS file.");
        }

        var formLength = ReadUInt32(input, 4);
        if (formLength < 4 || formLength + 8UL != (ulong)input.Length)
        {
            throw Invalid("The IFZS FORM length does not match the save-file length.");
        }

        var offset = 12;
        byte[]? identityData = null;
        byte[]? compressedMemory = null;
        byte[]? uncompressedMemory = null;
        byte[]? stackData = null;
        QuetzalInterpreterData? interpreterData = null;
        var annotations = new List<string>();
        var identitySeen = false;
        var memorySeen = false;
        var stackSeen = false;
        while (offset < input.Length)
        {
            if (input.Length - offset < 8)
            {
                throw Invalid("The save file ends inside an IFF chunk header.");
            }

            var idBytes = input.Slice(offset, 4);
            ValidateChunkId(idBytes);
            var length = ReadUInt32(input, offset + 4);
            var paddedLength = (ulong)length + (length & 1);
            var dataOffset = offset + 8;
            if (paddedLength > (ulong)(input.Length - dataOffset))
            {
                throw Invalid($"Chunk '{Encoding.ASCII.GetString(idBytes)}' extends beyond the IFZS FORM.");
            }

            var data = input.Slice(dataOffset, (int)length);
            if ((length & 1) != 0 && input[dataOffset + (int)length] != 0)
            {
                throw Invalid($"Chunk '{Encoding.ASCII.GetString(idBytes)}' has an invalid nonzero IFF padding byte.");
            }

            var id = Encoding.ASCII.GetString(idBytes);
            switch (id)
            {
                case "IFhd" when !identitySeen:
                    identitySeen = true;
                    identityData = data.ToArray();
                    break;
                case "CMem" when !memorySeen:
                    RequireIdentityBeforePayload(identitySeen, id);
                    memorySeen = true;
                    compressedMemory = data.ToArray();
                    break;
                case "UMem" when !memorySeen:
                    RequireIdentityBeforePayload(identitySeen, id);
                    memorySeen = true;
                    uncompressedMemory = data.ToArray();
                    break;
                case "Stks" when !stackSeen:
                    RequireIdentityBeforePayload(identitySeen, id);
                    stackSeen = true;
                    stackData = data.ToArray();
                    break;
                case "ANNO":
                    annotations.Add(ReadAscii(data, "ANNO"));
                    break;
                case "IntD" when interpreterData is null:
                    interpreterData = ReadInterpreterData(data);
                    break;
            }

            offset = checked(dataOffset + (int)paddedLength);
        }

        if (identityData is null || !memorySeen || stackData is null)
        {
            throw Invalid("The IFZS file must contain IFhd, one of CMem or UMem, and Stks chunks.");
        }

        var identity = ReadIdentity(identityData, story);
        var dynamicMemory = compressedMemory is not null
            ? DecompressMemory(compressedMemory, story.Data.Span[..story.Header.StaticMemoryBase])
            : ValidateUncompressedMemory(uncompressedMemory!, story.Header.StaticMemoryBase);
        var frames = ReadStack(stackData, story.Header.Version, story.Header.FileLength);
        ValidateProgramCounter(identity.ProgramCounter, story.Header.FileLength);
        return new QuetzalSaveDocument(
            identity.ReleaseNumber,
            identity.SerialNumber,
            identity.Checksum,
            identity.ProgramCounter,
            dynamicMemory,
            frames,
            annotations,
            interpreterData);
    }

    public static void Restore(ZMachineState state, QuetzalSaveDocument document)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocumentIdentity(document, state.LoadedStory);
        ValidateProgramCounter(document.ProgramCounter, state.Header.FileLength);
        if (document.DynamicMemory.Length != state.Memory.DynamicMemoryLength)
        {
            throw Invalid("The save document's dynamic-memory length does not match this story.");
        }

        var candidate = new ZMachineState(state.LoadedStory);
        candidate.RestoreSnapshot(document.DynamicMemory, document.StackFrames, document.ProgramCounter);
        candidate.CompleteRestoredSave(document.ProgramCounter);
        state.RestoreSnapshot(document.DynamicMemory, document.StackFrames, document.ProgramCounter);
        state.CompleteRestoredSave(document.ProgramCounter);
    }

    private static byte[] WriteIdentity(ZMachineState state, int resumeAddress, ReadOnlySpan<byte> originalStory)
    {
        var result = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(result, state.Header.ReleaseNumber);
        Encoding.ASCII.GetBytes(state.Header.SerialNumber, result.AsSpan(2, 6));
        var checksum = state.Header.Version >= 3 ? state.Header.Checksum : CalculateChecksum(originalStory, state.Header.FileLength);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), checksum);
        result[10] = (byte)(resumeAddress >> 16);
        result[11] = (byte)(resumeAddress >> 8);
        result[12] = (byte)resumeAddress;
        return result;
    }

    private static (ushort ReleaseNumber, string SerialNumber, ushort Checksum, int ProgramCounter) ReadIdentity(byte[] data, LoadedStory story)
    {
        if (data.Length < 13)
        {
            throw Invalid("The IFhd chunk must contain at least 13 bytes.");
        }

        var release = BinaryPrimitives.ReadUInt16BigEndian(data);
        var serial = Encoding.ASCII.GetString(data, 2, 6);
        var checksum = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8));
        var pc = (data[10] << 16) | (data[11] << 8) | data[12];
        var storyChecksum = story.Header.Version >= 3
            ? story.Header.Checksum
            : CalculateChecksum(story.Data.Span, story.Header.FileLength);
        if (release != story.Header.ReleaseNumber || serial != story.Header.SerialNumber || checksum != storyChecksum)
        {
            throw Invalid("The save file belongs to a different story (release, serial number, or checksum differs).");
        }

        return (release, serial, checksum, pc);
    }

    private static byte[] CompressMemory(ReadOnlySpan<byte> dynamicMemory, ReadOnlySpan<byte> original)
    {
        var encoded = new List<byte>(dynamicMemory.Length);
        for (var index = 0; index < dynamicMemory.Length;)
        {
            var difference = (byte)(dynamicMemory[index] ^ original[index]);
            if (difference != 0)
            {
                encoded.Add(difference);
                index++;
                continue;
            }

            var runLength = 1;
            while (runLength < 256 && index + runLength < dynamicMemory.Length
                && (dynamicMemory[index + runLength] ^ original[index + runLength]) == 0)
            {
                runLength++;
            }

            encoded.Add(0);
            encoded.Add((byte)(runLength - 1));
            index += runLength;
        }

        return encoded.ToArray();
    }

    private static byte[] DecompressMemory(byte[] encoded, ReadOnlySpan<byte> original)
    {
        var result = original.ToArray();
        var offset = 0;
        var index = 0;
        while (offset < encoded.Length)
        {
            var value = encoded[offset++];
            if (value == 0)
            {
                if (offset >= encoded.Length)
                {
                    throw Invalid("The CMem chunk ends with an incomplete zero run.");
                }

                var runLength = encoded[offset++] + 1;
                if (index + runLength > result.Length)
                {
                    throw Invalid("The CMem chunk expands beyond dynamic memory.");
                }

                index += runLength;
            }
            else
            {
                if (index >= result.Length)
                {
                    throw Invalid("The CMem chunk expands beyond dynamic memory.");
                }

                result[index] ^= value;
                index++;
            }
        }

        return result;
    }

    private static byte[] ValidateUncompressedMemory(byte[] data, int length)
    {
        if (data.Length != length)
        {
            throw Invalid($"The UMem chunk must contain exactly {length} bytes of dynamic memory.");
        }

        return data.ToArray();
    }

    private static byte[] WriteStack(ZMachineCallStackSnapshot snapshot, byte version)
    {
        using var stream = new MemoryStream();
        foreach (var frame in snapshot.Frames)
        {
            var isDummy = frame.IsRoot && version != 6;
            WriteUInt24(stream, isDummy ? 0 : frame.ReturnAddress);
            var localCount = isDummy ? 0 : frame.Locals.Length;
            var flags = (byte)((frame.StoreVariable is null && !isDummy ? 0x10 : 0) | localCount);
            stream.WriteByte(flags);
            stream.WriteByte(isDummy || frame.StoreVariable is null ? (byte)0 : frame.StoreVariable.Value);
            stream.WriteByte(isDummy ? (byte)0 : frame.ArgumentMask);
            WriteUInt16(stream, checked((ushort)frame.EvaluationStack.Count));
            if (!isDummy)
            {
                foreach (var local in frame.Locals)
                {
                    WriteUInt16(stream, local);
                }
            }

            foreach (var value in frame.EvaluationStack)
            {
                WriteUInt16(stream, value);
            }
        }

        return stream.ToArray();
    }

    private static IReadOnlyList<ZMachineStackFrameSnapshot> ReadStack(byte[] data, byte version, int storyLength)
    {
        var frames = new List<ZMachineStackFrameSnapshot>();
        var offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8)
            {
                throw Invalid("The Stks chunk ends inside a stack-frame header.");
            }

            var returnAddress = ReadUInt24(data, offset);
            var flags = data[offset + 3];
            if ((flags & 0xe0) != 0)
            {
                throw Invalid("A Stks frame has nonzero reserved flag bits.");
            }

            var discardResult = (flags & 0x10) != 0;
            var localCount = flags & 0x0f;
            var storeVariable = data[offset + 4];
            var argumentMask = data[offset + 5];
            var evaluationCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 6));
            var validArgumentMask = version <= 3 ? 0x07 : 0x7f;
            if ((argumentMask & ~validArgumentMask) != 0 || (discardResult && storeVariable != 0))
            {
                throw Invalid("A Stks frame has invalid argument bits or a result variable on a discard-result call.");
            }

            var payloadBytes = ((long)localCount + evaluationCount) * sizeof(ushort);
            var frameEnd = offset + 8L + payloadBytes;
            if (frameEnd > data.Length)
            {
                throw Invalid("A Stks frame's local or evaluation stack data is truncated.");
            }

            var isRoot = frames.Count == 0;
            var isDummy = isRoot && version != 6;
            if (isDummy)
            {
                if (returnAddress != 0 || flags != 0 || storeVariable != 0 || argumentMask != 0)
                {
                    throw Invalid("The non-V6 dummy stack frame contains nonzero fields other than its evaluation count.");
                }
            }
            else if (returnAddress >= storyLength || (isRoot && version == 6 && returnAddress != 0))
            {
                throw Invalid($"Stks frame {frames.Count} has an invalid return PC.");
            }

            var locals = new ushort[isDummy ? 0 : localCount];
            var values = new ushort[evaluationCount];
            var cursor = offset + 8;
            for (var index = 0; index < localCount; index++)
            {
                var value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
                if (!isDummy) locals[index] = value;
                cursor += 2;
            }

            for (var index = 0; index < values.Length; index++)
            {
                values[index] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(cursor));
                cursor += 2;
            }

            frames.Add(new ZMachineStackFrameSnapshot(
                isRoot,
                returnAddress,
                isRoot || discardResult ? null : storeVariable,
                isDummy ? (byte)0 : argumentMask,
                locals,
                values));
            offset = (int)frameEnd;
        }

        if (frames.Count == 0)
        {
            throw Invalid("The Stks chunk contains no stack frames.");
        }

        return frames;
    }

    private static byte[] WriteInterpreterData(QuetzalInterpreterData data)
    {
        if (!IsValidChunkId(data.OperatingSystemId) || !IsValidChunkId(data.InterpreterId)
            || (data.Flags & 0xfc) != 0 || (data.OperatingSystemId == "    " && data.InterpreterId == "    "))
        {
            throw new ArgumentException("IntD IDs and flags do not follow Quetzal's interpreter-data rules.", nameof(data));
        }

        var result = new byte[checked(12 + data.Data.Length)];
        Encoding.ASCII.GetBytes(data.OperatingSystemId, result.AsSpan(0, 4));
        result[4] = data.Flags;
        result[5] = data.ContentsId;
        Encoding.ASCII.GetBytes(data.InterpreterId, result.AsSpan(8, 4));
        data.Data.CopyTo(result, 12);
        return result;
    }

    private static QuetzalInterpreterData ReadInterpreterData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || data[6] != 0 || data[7] != 0 || (data[4] & 0xfc) != 0)
        {
            throw Invalid("The IntD chunk has an invalid length, flags, or reserved field.");
        }

        ValidateChunkId(data[..4]);
        ValidateChunkId(data.Slice(8, 4));
        var os = Encoding.ASCII.GetString(data[..4]);
        var interpreter = Encoding.ASCII.GetString(data.Slice(8, 4));
        if (os == "    " && interpreter == "    ")
        {
            throw Invalid("IntD may not use blank operating-system and interpreter IDs together.");
        }

        return new QuetzalInterpreterData(os, interpreter, data[5], data[4], data[12..].ToArray());
    }

    private static byte[] WriteForm(IEnumerable<(string Id, byte[] Data)> chunks)
    {
        using var stream = new MemoryStream();
        stream.Write(FormId);
        WriteUInt32(stream, 0);
        stream.Write(IfzsId);
        foreach (var (id, data) in chunks)
        {
            if (id.Length != 4 || data.LongLength > uint.MaxValue)
            {
                throw new ArgumentException("An IFF chunk ID or length is invalid.", nameof(chunks));
            }

            stream.Write(Encoding.ASCII.GetBytes(id));
            WriteUInt32(stream, (uint)data.Length);
            stream.Write(data);
            if ((data.Length & 1) != 0) stream.WriteByte(0);
        }

        var result = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static void RequireIdentityBeforePayload(bool identitySeen, string id)
    {
        if (!identitySeen) throw Invalid($"The IFhd chunk must precede the '{id}' chunk.");
    }

    private static string ReadAscii(ReadOnlySpan<byte> data, string id)
    {
        foreach (var value in data)
        {
            if (value is < 0x20 or > 0x7e)
            {
                throw Invalid($"The {id} chunk contains non-printable ASCII data.");
            }
        }

        return Encoding.ASCII.GetString(data);
    }

    private static void ValidateChunkId(ReadOnlySpan<byte> id)
    {
        var sawSpace = false;
        foreach (var value in id)
        {
            if (value is < 0x20 or > 0x7e || (sawSpace && value != 0x20))
            {
                throw Invalid("An IFF chunk ID is not four printable characters with only trailing spaces.");
            }

            sawSpace |= value == 0x20;
        }
    }

    private static bool IsValidChunkId(string id)
    {
        if (id.Length != 4) return false;
        var sawSpace = false;
        foreach (var character in id)
        {
            if (character is < ' ' or > '~' || (sawSpace && character != ' ')) return false;
            sawSpace |= character == ' ';
        }

        return true;
    }

    private static ushort CalculateChecksum(ReadOnlySpan<byte> story, int fileLength)
    {
        uint sum = 0;
        foreach (var value in story.Slice(64, fileLength - 64)) sum += value;
        return (ushort)sum;
    }

    private static void ValidateDocumentIdentity(QuetzalSaveDocument document, LoadedStory story)
    {
        var checksum = story.Header.Version >= 3
            ? story.Header.Checksum
            : CalculateChecksum(story.Data.Span, story.Header.FileLength);
        if (document.ReleaseNumber != story.Header.ReleaseNumber
            || document.SerialNumber != story.Header.SerialNumber
            || document.Checksum != checksum)
        {
            throw Invalid("The save file belongs to a different story (release, serial number, or checksum differs).");
        }
    }

    private static void ValidateProgramCounter(int address, int storyLength)
    {
        if ((uint)address >= (uint)storyLength || address > 0xffffff)
        {
            throw Invalid($"The saved program counter 0x{address:x} is outside the story file.");
        }
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data, int offset) => (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];

    private static void WriteUInt24(Stream stream, int value)
    {
        if (value is < 0 or > 0xffffff) throw new ArgumentOutOfRangeException(nameof(value));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static QuetzalSaveFileException Invalid(string message) => new(message);
}

public sealed record QuetzalSaveDocument(
    ushort ReleaseNumber,
    string SerialNumber,
    ushort Checksum,
    int ProgramCounter,
    byte[] DynamicMemory,
    IReadOnlyList<ZMachineStackFrameSnapshot> StackFrames,
    IReadOnlyList<string> Annotations,
    QuetzalInterpreterData? InterpreterData);

public sealed record QuetzalInterpreterData(
    string OperatingSystemId,
    string InterpreterId,
    byte ContentsId,
    byte Flags,
    byte[] Data);

public sealed class QuetzalSaveFileException(string message) : Exception(message);
