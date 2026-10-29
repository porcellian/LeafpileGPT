using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LeafpileGPT.Core;

/// <summary>A version-aware field decoded from both the original and current runtime header.</summary>
public sealed record StoryFileInfoHeaderField(
    string Name,
    string Meaning,
    string OriginalValue,
    string RuntimeValue);

public sealed record StoryFileInfoResourceCount(string ChunkType, int Count);

/// <summary>Immutable data for the read-only Story File Info window.</summary>
public sealed record StoryFileInfoSnapshot(
    string SourcePath,
    string SourceFileName,
    string SourceFormat,
    long SourceFileSizeBytes,
    string SourceFileSha256,
    long LoadedStorySizeBytes,
    string LoadedStorySha256,
    byte Version,
    ushort ReleaseNumber,
    string SerialNumber,
    string Ifid,
    string Title,
    string Author,
    string ChecksumStatus,
    ushort? StoredChecksum,
    ushort? CalculatedChecksum,
    ushort? DeclaredLengthWord,
    int? DeclaredStorySizeBytes,
    int EffectiveStorySizeBytes,
    ushort InitialProgramCounterValue,
    uint InitialProgramCounterByteAddress,
    bool UsesPackedV6MainRoutine,
    ushort DynamicMemoryEndExclusive,
    ushort StaticMemoryStart,
    ushort HighMemoryStart,
    int StoryImageEndExclusive,
    ushort DictionaryAddress,
    ushort ObjectTableAddress,
    ushort GlobalsTableAddress,
    ushort AbbreviationsTableAddress,
    ushort? AlphabetTableAddress,
    ushort? HeaderExtensionTableAddress,
    ImmutableArray<StoryFileInfoHeaderField> HeaderFields,
    ImmutableArray<string> OriginalRawHeaderLines,
    ImmutableArray<string> RuntimeRawHeaderLines,
    bool IsBlorb,
    string BlorbGameIdentifier,
    string IfmdStatus,
    string BlorbReleaseNumber,
    string Frontispiece,
    string SelectedExecutable,
    ImmutableArray<StoryFileInfoResourceCount> ResourceCounts)
{
    public static StoryFileInfoSnapshot Create(
        string sourcePath,
        StoryFile story,
        ImmutableArray<byte> runtimeHeader,
        BlorbFile? blorb = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(story);
        if (runtimeHeader.IsDefault || runtimeHeader.Length != StoryFile.HeaderLength)
        {
            throw new ArgumentException($"The runtime header snapshot must contain exactly {StoryFile.HeaderLength} bytes.", nameof(runtimeHeader));
        }

        var original = story.Header.RawHeader.ToArray();
        var runtime = runtimeHeader.ToArray();
        var header = story.Header;
        var sourceLength = blorb?.SourceFileSizeBytes ?? story.SourceFileSizeBytes;
        var sourceHash = blorb?.SourceFileSha256 ?? story.SourceFileSha256;
        var metadata = ParseMetadata(blorb?.Metadata ?? ImmutableArray<byte>.Empty);
        var version = header.Version;
        var declaredLength = header.FileLengthWord is > 0
            ? checked(header.FileLengthWord.Value * (version <= 3 ? 2 : version <= 5 ? 4 : 8))
            : (int?)null;

        return new StoryFileInfoSnapshot(
            Path.GetFullPath(sourcePath),
            Path.GetFileName(sourcePath),
            blorb is null ? "Standalone Z-machine story" : "Blorb container (IFF FORM IFRS)",
            sourceLength,
            sourceHash,
            story.Length,
            story.LoadedStorySha256,
            version,
            header.ReleaseNumber,
            header.SerialNumber,
            metadata.Ifid,
            metadata.Title,
            metadata.Author,
            ChecksumStatusLabel(header.ChecksumStatus),
            header.StoredChecksum,
            header.CalculatedChecksum,
            header.FileLengthWord,
            declaredLength,
            story.Length,
            header.InitialProgramCounter,
            ZMachineAddressing.InitialProgramCounterByteAddress(header),
            header.UsesPackedMainRoutine,
            header.StaticMemoryBase,
            header.StaticMemoryBase,
            header.HighMemoryBase,
            story.Length,
            header.DictionaryAddress,
            header.ObjectTableAddress,
            header.GlobalsTableAddress,
            header.AbbreviationsTableAddress,
            header.AlphabetTableAddress,
            header.HeaderExtensionTableAddress,
            BuildHeaderFields(version, original, runtime),
            FormatRawHeader(original),
            FormatRawHeader(runtime),
            blorb is not null,
            blorb is null ? NotPresent : FormatHex(blorb.GameIdentifier.AsSpan()),
            blorb is null || blorb.Metadata.IsDefaultOrEmpty ? NotPresent : $"Present ({blorb.Metadata.Length} bytes)",
            blorb is null || blorb.FindChunk("RelN") is null ? NotPresent : blorb.ReleaseNumber.ToString(),
            blorb?.FrontispieceNumber is { } frontispiece ? frontispiece.ToString() : NotPresent,
            FormatSelectedExecutable(blorb?.SelectedExecutable),
            blorb is null
                ? []
                : blorb.Resources
                    .GroupBy(resource => resource.Chunk.Type, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new StoryFileInfoResourceCount(group.Key, group.Count()))
                    .ToImmutableArray());
    }

    private const string NotPresent = "Not present";

    private static ImmutableArray<StoryFileInfoHeaderField> BuildHeaderFields(byte version, byte[] original, byte[] runtime)
    {
        var fields = ImmutableArray.CreateBuilder<StoryFileInfoHeaderField>();
        Add("Version", "Z-machine version", value => value[0].ToString(), minimumVersion: 1);
        Add("Flags 1", "Version-specific story and interpreter flags", value => $"0x{value[1]:X2} ({DescribeFlags1(version, value[1])})", minimumVersion: 1);
        Add("Release number", "Story release identifier", value => ReadWord(value, 0x02).ToString(), minimumVersion: 1);
        Add("High memory base", "Byte address where high memory begins", value => FormatAddress(ReadWord(value, 0x04)), minimumVersion: 1);
        Add("Initial PC", version == 6 ? "Packed main routine address" : "Initial byte program counter", value => FormatAddress(ReadWord(value, 0x06)), minimumVersion: 1);
        Add("Dictionary table", "Byte address", value => FormatAddress(ReadWord(value, 0x08)), minimumVersion: 1);
        Add("Object table", "Byte address", value => FormatAddress(ReadWord(value, 0x0A)), minimumVersion: 1);
        Add("Global variables", "Byte address", value => FormatAddress(ReadWord(value, 0x0C)), minimumVersion: 1);
        Add("Static memory base", "Byte address where static memory begins", value => FormatAddress(ReadWord(value, 0x0E)), minimumVersion: 1);
        Add("Flags 2", "Game and interpreter state flags", value => $"0x{ReadWord(value, 0x10):X4} ({DescribeFlags2(version, ReadWord(value, 0x10))})", minimumVersion: 1);
        Add("Serial number", "Six-byte story serial", value => Encoding.Latin1.GetString(value, 0x12, 6), minimumVersion: 1);
        Add("Abbreviations table", "Byte address", value => FormatAddress(ReadWord(value, 0x18)), minimumVersion: 2);
        Add("Declared file length", "Header length word and version-scaled byte count", value => FormatDeclaredLength(version, ReadWord(value, 0x1A)), minimumVersion: 3);
        Add("Checksum", "Stored checksum in the header", value => $"0x{ReadWord(value, 0x1C):X4}", minimumVersion: 3);
        Add("Interpreter number", "Interpreter identifier", value => DescribeInterpreter(value[0x1E]), minimumVersion: 4);
        Add("Interpreter version", "Interpreter version byte", value => $"{Printable(value[0x1F])} (0x{value[0x1F]:X2})", minimumVersion: 4);
        Add("Screen height", "Lines; 255 means unlimited", value => value[0x20].ToString(), minimumVersion: 4);
        Add("Screen width", "Characters", value => value[0x21].ToString(), minimumVersion: 4);
        Add("Screen width units", "Screen units", value => ReadWord(value, 0x22).ToString(), minimumVersion: 5);
        Add("Screen height units", "Screen units", value => ReadWord(value, 0x24).ToString(), minimumVersion: 5);
        Add("Font width", "Units; width of the '0' character", value => value[version == 6 ? 0x27 : 0x26].ToString(), minimumVersion: 5);
        Add("Font height", "Units", value => value[version == 6 ? 0x26 : 0x27].ToString(), minimumVersion: 5);
        Add("Routines offset", "Packed-address offset, divided by 8", value => ReadWord(value, 0x28).ToString(), minimumVersion: 6, maximumVersion: 7);
        Add("Strings offset", "Packed-address offset, divided by 8", value => ReadWord(value, 0x2A).ToString(), minimumVersion: 6, maximumVersion: 7);
        Add("Default background colour", "Z-machine colour number", value => DescribeColour(value[0x2C]), minimumVersion: 5);
        Add("Default foreground colour", "Z-machine colour number", value => DescribeColour(value[0x2D]), minimumVersion: 5);
        Add("Terminating characters table", "Byte address", value => FormatAddress(ReadWord(value, 0x2E)), minimumVersion: 5);
        Add("Output stream 3 pixel width", "Total text width in pixels", value => ReadWord(value, 0x30).ToString(), minimumVersion: 6);
        Add("Standard revision", "Z-machine standard revision", value => $"{value[0x32]}.{value[0x33]}", minimumVersion: 1);
        Add("Alphabet table", "Byte address; zero selects the default", value => FormatAddress(ReadWord(value, 0x34)), minimumVersion: 5);
        Add("Header extension table", "Byte address", value => FormatAddress(ReadWord(value, 0x36)), minimumVersion: 5);
        return fields.ToImmutable();

        void Add(string name, string meaning, Func<byte[], string> formatter, byte minimumVersion, byte maximumVersion = byte.MaxValue)
        {
            if (version >= minimumVersion && version <= maximumVersion)
            {
                fields.Add(new StoryFileInfoHeaderField(name, meaning, formatter(original), formatter(runtime)));
            }
        }
    }

    private static string DescribeFlags1(byte version, byte flags)
    {
        var values = new List<string>();
        if (version <= 3)
        {
            if (version == 3)
            {
                values.Add($"status line {((flags & 0x02) != 0 ? "hours/minutes" : "score/turns")}");
            }

            values.Add($"split-disc story: {YesNo(flags, 0x04)}");
            if (version >= 3)
            {
                values.Add($"status line unavailable: {YesNo(flags, 0x10)}");
                values.Add($"screen splitting available: {YesNo(flags, 0x20)}");
                values.Add($"variable-pitch default: {YesNo(flags, 0x40)}");
            }
        }
        else
        {
            AddFlag(values, flags, 0x01, "colours available", version >= 5);
            AddFlag(values, flags, 0x02, "picture display available", version >= 6);
            AddFlag(values, flags, 0x04, "bold available", true);
            AddFlag(values, flags, 0x08, "italic available", true);
            AddFlag(values, flags, 0x10, "fixed-space style available", true);
            AddFlag(values, flags, 0x20, "sound effects available", version >= 6);
            AddFlag(values, flags, 0x80, "timed input available", true);
        }

        return string.Join("; ", values);
    }

    private static string DescribeFlags2(byte version, ushort flags)
    {
        var values = new List<string>();
        AddFlag(values, flags, 0x0001, "transcription active", true);
        AddFlag(values, flags, 0x0002, "fixed-pitch requested", version >= 3);
        AddFlag(values, flags, 0x0004, "screen redraw requested", version >= 6);
        AddFlag(values, flags, 0x0008, "pictures requested", version >= 5);
        AddFlag(values, flags, 0x0010, "undo requested", version >= 5);
        AddFlag(values, flags, 0x0020, "mouse requested", version >= 5);
        AddFlag(values, flags, 0x0040, "colours requested", version >= 5);
        AddFlag(values, flags, 0x0080, "sound effects requested", version >= 5);
        AddFlag(values, flags, 0x0100, "menus requested", version >= 6);
        return string.Join("; ", values);
    }

    private static void AddFlag(List<string> values, ushort flags, ushort mask, string label, bool applicable)
    {
        if (applicable)
        {
            values.Add($"{label}: {((flags & mask) != 0 ? "Yes" : "No")}");
        }
    }

    private static void AddFlag(List<string> values, byte flags, byte mask, string label, bool applicable)
    {
        if (applicable)
        {
            values.Add($"{label}: {YesNo(flags, mask)}");
        }
    }

    private static (string Ifid, string Title, string Author) ParseMetadata(ImmutableArray<byte> metadata)
    {
        if (metadata.IsDefaultOrEmpty)
        {
            return (NotPresent, NotPresent, NotPresent);
        }

        try
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(metadata.AsSpan()));
            var ifid = FindValues("ifid");
            var title = FindValues("title");
            var author = FindValues("author");
            return (Optional(ifid), Optional(title), Optional(author));

            string FindValues(string elementName) => string.Join(", ", document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value.Trim())
                .Where(value => value.Length > 0));
        }
        catch (XmlException)
        {
            return (NotPresent, NotPresent, NotPresent);
        }
    }

    private static ImmutableArray<string> FormatRawHeader(byte[] header) => Enumerable.Range(0, StoryFile.HeaderLength / 16)
        .Select(row =>
        {
            var offset = row * 16;
            var bytes = Convert.ToHexString(header.AsSpan(offset, 16)).Chunk(2).Select(pair => new string(pair));
            return $"{offset:X2}: {string.Join(' ', bytes)}";
        })
        .ToImmutableArray();

    private static string FormatSelectedExecutable(BlorbResource? executable) => executable is null
        ? NotPresent
        : $"{executable.Chunk.Type} resource {executable.Number} at 0x{executable.Start:X6}";

    private static string FormatDeclaredLength(byte version, ushort lengthWord)
    {
        if (lengthWord == 0)
        {
            return version == 3 ? NotPresent : "0 (invalid for this version)";
        }

        var multiplier = version <= 3 ? 2 : version <= 5 ? 4 : 8;
        return $"{lengthWord} units = {lengthWord * multiplier} bytes";
    }

    private static string DescribeInterpreter(byte number)
    {
        var name = number switch
        {
            1 => "DECsystem-20",
            2 => "Apple IIe",
            3 => "Macintosh",
            4 => "Amiga",
            5 => "Atari ST",
            6 => "IBM PC",
            7 => "Commodore 128",
            8 => "Apple IIc",
            9 => "Apple IIgs",
            10 => "Tandy Color Computer",
            _ => "unknown interpreter"
        };
        return $"{number} ({name}, 0x{number:X2})";
    }

    private static string DescribeColour(byte number)
    {
        var name = number switch
        {
            0 => "current",
            1 => "default",
            2 => "black",
            3 => "red",
            4 => "green",
            5 => "yellow",
            6 => "blue",
            7 => "magenta",
            8 => "cyan",
            9 => "white",
            10 => "light grey",
            11 => "medium grey",
            12 => "dark grey",
            _ => "unknown colour"
        };
        return $"{number} ({name}, 0x{number:X2})";
    }

    private static string ChecksumStatusLabel(StoryChecksumStatus status) => status switch
    {
        StoryChecksumStatus.Valid => "Valid (stored and calculated values match)",
        StoryChecksumStatus.UnavailableInLegacyVersion3 => "Unavailable (legacy Version 3 header)",
        _ => "Not defined for this version"
    };

    private static string YesNo(byte flags, byte mask) => (flags & mask) != 0 ? "Yes" : "No";

    private static string FormatAddress(ushort address) => $"0x{address:X4}";

    private static ushort ReadWord(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, sizeof(ushort)));

    private static string Printable(byte value) => value is >= 0x20 and <= 0x7E ? $"'{(char)value}'" : $"0x{value:X2}";

    private static string Optional(string value) => string.IsNullOrWhiteSpace(value) ? NotPresent : value;

    private static string FormatHex(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? NotPresent : Convert.ToHexString(bytes);
}
