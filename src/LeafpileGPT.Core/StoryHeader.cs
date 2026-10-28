namespace LeafpileGPT.Core;

using System.Buffers.Binary;

public enum StoryChecksumStatus
{
    NotDefined,
    UnavailableInLegacyVersion3,
    Valid
}

public sealed class StoryHeader
{
    private readonly byte[] _rawHeader;

    internal StoryHeader(
        ReadOnlySpan<byte> rawHeader,
        byte version,
        ushort releaseNumber,
        ushort highMemoryBase,
        ushort initialProgramCounter,
        ushort dictionaryAddress,
        ushort objectTableAddress,
        ushort globalsTableAddress,
        ushort staticMemoryBase,
        string serialNumber,
        ushort? fileLengthWord,
        int fileLength,
        ushort? storedChecksum,
        ushort? calculatedChecksum,
        StoryChecksumStatus checksumStatus)
    {
        _rawHeader = rawHeader.ToArray();
        Version = version;
        ReleaseNumber = releaseNumber;
        HighMemoryBase = highMemoryBase;
        InitialProgramCounter = initialProgramCounter;
        DictionaryAddress = dictionaryAddress;
        ObjectTableAddress = objectTableAddress;
        GlobalsTableAddress = globalsTableAddress;
        StaticMemoryBase = staticMemoryBase;
        SerialNumber = serialNumber;
        FileLengthWord = fileLengthWord;
        FileLength = fileLength;
        StoredChecksum = storedChecksum;
        CalculatedChecksum = calculatedChecksum;
        ChecksumStatus = checksumStatus;
    }

    public byte Version { get; }

    public ushort ReleaseNumber { get; }

    public ushort HighMemoryBase { get; }

    /// <summary>
    /// Gets the initial byte program counter, or the V6 packed main-routine address.
    /// </summary>
    public ushort InitialProgramCounter { get; }

    public ushort DictionaryAddress { get; }

    public ushort ObjectTableAddress { get; }

    public ushort GlobalsTableAddress { get; }

    public ushort StaticMemoryBase { get; }

    public byte Flags1 => _rawHeader[0x01];

    public ushort Flags2 => ReadWord(0x10);

    public string SerialNumber { get; }

    public ushort? FileLengthWord { get; }

    /// <summary>Gets the effective story length in bytes, excluding optional file padding.</summary>
    public int FileLength { get; }

    public ushort? StoredChecksum { get; }

    public ushort? CalculatedChecksum { get; }

    public StoryChecksumStatus ChecksumStatus { get; }

    public byte? InterpreterNumber => Version >= 4 ? _rawHeader[0x1E] : null;

    public byte? InterpreterVersion => Version >= 4 ? _rawHeader[0x1F] : null;

    public byte? ScreenHeightInLines => Version >= 4 ? _rawHeader[0x20] : null;

    public byte? ScreenWidthInCharacters => Version >= 4 ? _rawHeader[0x21] : null;

    public ushort? ScreenWidthInUnits => Version >= 5 ? ReadWord(0x22) : null;

    public ushort? ScreenHeightInUnits => Version >= 5 ? ReadWord(0x24) : null;

    public byte? FontWidthInUnits => Version >= 5 ? _rawHeader[Version == 6 ? 0x27 : 0x26] : null;

    public byte? FontHeightInUnits => Version >= 5 ? _rawHeader[Version == 6 ? 0x26 : 0x27] : null;

    /// <summary>Gets the routines offset used to unpack V6/V7 routine addresses.</summary>
    public ushort? RoutinesOffset => Version >= 6 ? ReadWord(0x28) : null;

    /// <summary>Gets the strings offset used to unpack V6/V7 string addresses.</summary>
    public ushort? StringsOffset => Version >= 6 ? ReadWord(0x2A) : null;

    public byte? DefaultBackgroundColour => Version >= 5 ? _rawHeader[0x2C] : null;

    public byte? DefaultForegroundColour => Version >= 5 ? _rawHeader[0x2D] : null;

    public ushort? TerminatingCharactersTableAddress => Version >= 5 ? ReadWord(0x2E) : null;

    public ushort? OutputStream3PixelWidth => Version >= 6 ? ReadWord(0x30) : null;

    public byte StandardRevisionMajor => _rawHeader[0x32];

    public byte StandardRevisionMinor => _rawHeader[0x33];

    public ushort? AlphabetTableAddress => Version >= 5 ? ReadWord(0x34) : null;

    public ushort? HeaderExtensionTableAddress => Version >= 5 ? ReadWord(0x36) : null;

    public ReadOnlyMemory<byte> RawHeader => _rawHeader;

    public bool UsesPackedMainRoutine => Version == 6;

    private ushort ReadWord(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_rawHeader.AsSpan(offset, sizeof(ushort)));
}
