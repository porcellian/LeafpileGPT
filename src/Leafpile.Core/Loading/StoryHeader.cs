using System.Text;

namespace Leafpile.Core.Loading;

public sealed record StoryHeader
{
    public byte Version { get; private init; }

    public byte Flags1 { get; private init; }

    public ushort HighMemoryBase { get; private init; }

    public ushort RawInitialAddress { get; private init; }

    public int InitialProgramAddress { get; private init; }

    public ushort DictionaryAddress { get; private init; }

    public ushort ObjectTableAddress { get; private init; }

    public ushort GlobalVariablesAddress { get; private init; }

    public ushort StaticMemoryBase { get; private init; }

    public ushort Flags2 { get; private init; }

    public ushort AbbreviationsTableAddress { get; private init; }

    public int FileLength { get; private init; }

    public ushort Checksum { get; private init; }

    public ushort ReleaseNumber { get; private init; }

    public string SerialNumber { get; private init; } = string.Empty;

    public ushort RoutinesOffset { get; private init; }

    public ushort StringsOffset { get; private init; }

    internal static StoryHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 64)
        {
            throw new StoryLoadException($"The story header is incomplete: expected at least 64 bytes, found {data.Length}.");
        }

        var version = data[0];
        if (version is < 1 or > 8)
        {
            throw new StoryLoadException($"Unsupported Z-machine version {version}; supported versions are 1 through 8.");
        }

        var fileLength = DetermineFileLength(data, version);
        ValidateFileLength(data.Length, fileLength, version);

        var highMemoryBase = ReadWord(data, 0x04);
        var rawInitialAddress = ReadWord(data, 0x06);
        var dictionaryAddress = ReadWord(data, 0x08);
        var objectTableAddress = ReadWord(data, 0x0a);
        var globalVariablesAddress = ReadWord(data, 0x0c);
        var staticMemoryBase = ReadWord(data, 0x0e);
        var abbreviationsTableAddress = version >= 2 ? ReadWord(data, 0x18) : (ushort)0;
        var routinesOffset = version >= 6 ? ReadWord(data, 0x28) : (ushort)0;
        var stringsOffset = version >= 6 ? ReadWord(data, 0x2a) : (ushort)0;

        ValidateMemoryLayout(
            data,
            fileLength,
            version,
            highMemoryBase,
            rawInitialAddress,
            dictionaryAddress,
            objectTableAddress,
            globalVariablesAddress,
            staticMemoryBase,
            abbreviationsTableAddress,
            routinesOffset);

        var initialProgramAddress = version == 6
            ? checked((rawInitialAddress * 4) + (routinesOffset * 8))
            : rawInitialAddress;

        return new StoryHeader
        {
            Version = version,
            Flags1 = data[1],
            HighMemoryBase = highMemoryBase,
            RawInitialAddress = rawInitialAddress,
            InitialProgramAddress = initialProgramAddress,
            DictionaryAddress = dictionaryAddress,
            ObjectTableAddress = objectTableAddress,
            GlobalVariablesAddress = globalVariablesAddress,
            StaticMemoryBase = staticMemoryBase,
            Flags2 = ReadWord(data, 0x10),
            AbbreviationsTableAddress = abbreviationsTableAddress,
            FileLength = fileLength,
            ReleaseNumber = ReadWord(data, 0x02),
            Checksum = version >= 3 ? ReadWord(data, 0x1c) : (ushort)0,
            SerialNumber = Encoding.ASCII.GetString(data.Slice(0x12, 6)),
            RoutinesOffset = routinesOffset,
            StringsOffset = stringsOffset
        };
    }

    private static int DetermineFileLength(ReadOnlySpan<byte> data, byte version)
    {
        if (version < 3)
        {
            return data.Length;
        }

        var lengthUnits = ReadWord(data, 0x1a);
        if (lengthUnits == 0)
        {
            if (version == 3)
            {
                // The standard notes that some early V3 stories lack this field.
                return data.Length;
            }

            throw new StoryLoadException($"The V{version} story header has no file length.");
        }

        var multiplier = version switch
        {
            <= 3 => 2,
            <= 5 => 4,
            _ => 8
        };

        return lengthUnits * multiplier;
    }

    private static void ValidateFileLength(int actualLength, int fileLength, byte version)
    {
        var maximumLength = version switch
        {
            <= 3 => 128 * 1024,
            <= 5 => 256 * 1024,
            _ => 512 * 1024
        };

        if (fileLength < 64)
        {
            throw new StoryLoadException($"The declared V{version} story length {fileLength} is shorter than its 64-byte header.");
        }

        if (fileLength > maximumLength)
        {
            throw new StoryLoadException($"The V{version} story length {fileLength} exceeds the standard limit of {maximumLength} bytes.");
        }

        if (actualLength < fileLength)
        {
            throw new StoryLoadException($"The story is truncated: its header declares {fileLength} bytes, but only {actualLength} are available.");
        }
    }

    private static void ValidateMemoryLayout(
        ReadOnlySpan<byte> data,
        int fileLength,
        byte version,
        ushort highMemoryBase,
        ushort rawInitialAddress,
        ushort dictionaryAddress,
        ushort objectTableAddress,
        ushort globalVariablesAddress,
        ushort staticMemoryBase,
        ushort abbreviationsTableAddress,
        ushort routinesOffset)
    {
        if (staticMemoryBase < 64)
        {
            throw new StoryLoadException($"Invalid static memory base 0x{staticMemoryBase:x4}; dynamic memory must contain at least 64 bytes.");
        }

        if (staticMemoryBase > fileLength)
        {
            throw new StoryLoadException($"Static memory base 0x{staticMemoryBase:x4} is beyond the end of the story.");
        }

        if (highMemoryBase < staticMemoryBase || highMemoryBase > fileLength)
        {
            throw new StoryLoadException($"Invalid high memory base 0x{highMemoryBase:x4}; it must be at or above static memory and no later than the end of the story.");
        }

        ValidateAddress(dictionaryAddress, fileLength, "dictionary");
        if (dictionaryAddress < staticMemoryBase)
        {
            throw new StoryLoadException($"Invalid dictionary address 0x{dictionaryAddress:x4}; the dictionary must be in static memory.");
        }

        var propertyDefaultsLength = version <= 3 ? 31 * 2 : 63 * 2;
        if (objectTableAddress < 64 || objectTableAddress + propertyDefaultsLength > staticMemoryBase)
        {
            throw new StoryLoadException($"Invalid object table address 0x{objectTableAddress:x4}; the property defaults table must fit in dynamic memory.");
        }

        const int globalTableLength = 240 * 2;
        if (globalVariablesAddress < 64 || globalVariablesAddress + globalTableLength > staticMemoryBase)
        {
            throw new StoryLoadException($"Invalid global variables address 0x{globalVariablesAddress:x4}; the 480-byte global table must fit in dynamic memory.");
        }

        if (version >= 2 && (abbreviationsTableAddress < 64 || abbreviationsTableAddress + (96 * 2) > fileLength))
        {
            throw new StoryLoadException($"Invalid abbreviations table address 0x{abbreviationsTableAddress:x4}; the 192-byte table must fit in the story file.");
        }

        int initialProgramAddress;
        if (version == 6)
        {
            initialProgramAddress = checked((rawInitialAddress * 4) + (routinesOffset * 8));
        }
        else
        {
            initialProgramAddress = rawInitialAddress;
        }

        if (initialProgramAddress < 64 || initialProgramAddress >= fileLength)
        {
            throw new StoryLoadException($"Invalid initial program address 0x{initialProgramAddress:x}; it is outside the story file.");
        }

        if (version == 6 && data[initialProgramAddress] > 15)
        {
            throw new StoryLoadException($"Invalid V6 main routine at 0x{initialProgramAddress:x}: local variable count exceeds 15.");
        }
    }

    private static void ValidateAddress(ushort address, int fileLength, string name)
    {
        if (address < 64 || address >= fileLength)
        {
            throw new StoryLoadException($"Invalid {name} address 0x{address:x4}; it must point inside the story after the header.");
        }
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);
}
