using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace Leafpile.Core.Loading;

/// <summary>A bounded chunk contained in an IFRS resource file.</summary>
public sealed class BlorbChunk
{
    internal BlorbChunk(string id, int offset, ReadOnlyMemory<byte> data)
    {
        Id = id;
        Offset = offset;
        Data = data;
    }

    public string Id { get; }

    /// <summary>Gets the offset of this chunk's eight-byte header from the start of the file.</summary>
    public int Offset { get; }

    /// <summary>Gets the chunk data, excluding its header and IFF pad byte.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}

/// <summary>A resource-index entry linked to its underlying chunk.</summary>
public sealed record BlorbResource(string Usage, uint Number, BlorbChunk Chunk);

public sealed record BlorbColor(byte Red, byte Green, byte Blue);

public sealed record BlorbResourceDescription(string Usage, uint Number, string Text);

public sealed record BlorbResolutionEntry(
    uint PictureNumber,
    uint StandardNumerator,
    uint StandardDenominator,
    uint MinimumNumerator,
    uint MinimumDenominator,
    uint MaximumNumerator,
    uint MaximumDenominator);

/// <summary>Validated optional metadata and display/audio hint chunks.</summary>
public sealed class BlorbMetadata
{
    internal BlorbMetadata(
        ushort? releaseNumber,
        ReadOnlyMemory<byte>? gameIdentifier,
        string? author,
        string? copyright,
        string? annotation,
        string? legacyStoryName,
        ReadOnlyMemory<byte>? xml,
        uint? frontispieceNumber,
        byte? preferredColorDepth,
        IReadOnlyList<BlorbColor> palette,
        IReadOnlyList<BlorbResourceDescription> resourceDescriptions,
        uint? standardWindowWidth,
        uint? standardWindowHeight,
        uint? minimumWindowWidth,
        uint? minimumWindowHeight,
        uint? maximumWindowWidth,
        uint? maximumWindowHeight,
        IReadOnlyList<BlorbResolutionEntry> resolutions,
        bool hasAdaptivePalette,
        IReadOnlySet<uint> adaptivePalettePictures,
        bool hasVersion3SoundLooping,
        IReadOnlyDictionary<uint, uint> version3SoundRepeats)
    {
        ReleaseNumber = releaseNumber;
        GameIdentifier = gameIdentifier;
        Author = author;
        Copyright = copyright;
        Annotation = annotation;
        LegacyStoryName = legacyStoryName;
        Xml = xml;
        FrontispieceNumber = frontispieceNumber;
        PreferredColorDepth = preferredColorDepth;
        Palette = palette;
        ResourceDescriptions = resourceDescriptions;
        StandardWindowWidth = standardWindowWidth;
        StandardWindowHeight = standardWindowHeight;
        MinimumWindowWidth = minimumWindowWidth;
        MinimumWindowHeight = minimumWindowHeight;
        MaximumWindowWidth = maximumWindowWidth;
        MaximumWindowHeight = maximumWindowHeight;
        Resolutions = resolutions;
        HasAdaptivePalette = hasAdaptivePalette;
        AdaptivePalettePictures = adaptivePalettePictures;
        HasVersion3SoundLooping = hasVersion3SoundLooping;
        Version3SoundRepeats = version3SoundRepeats;
    }

    public ushort? ReleaseNumber { get; }

    /// <summary>Gets the raw game identifier bytes, when an IFhd chunk is present.</summary>
    public ReadOnlyMemory<byte>? GameIdentifier { get; }

    public string? Author { get; }

    public string? Copyright { get; }

    public string? Annotation { get; }

    public string? LegacyStoryName { get; }

    /// <summary>Gets the raw UTF-8 IFmd XML document, when present.</summary>
    public ReadOnlyMemory<byte>? Xml { get; }

    public uint? FrontispieceNumber { get; }

    /// <summary>Gets a preferred direct-color depth, or null when an explicit palette is used.</summary>
    public byte? PreferredColorDepth { get; }

    public IReadOnlyList<BlorbColor> Palette { get; }

    public IReadOnlyList<BlorbResourceDescription> ResourceDescriptions { get; }

    public uint? StandardWindowWidth { get; }

    public uint? StandardWindowHeight { get; }

    public uint? MinimumWindowWidth { get; }

    public uint? MinimumWindowHeight { get; }

    public uint? MaximumWindowWidth { get; }

    public uint? MaximumWindowHeight { get; }

    public IReadOnlyList<BlorbResolutionEntry> Resolutions { get; }

    /// <summary>Gets whether the Blorb contains APal, including an empty APal chunk.</summary>
    public bool HasAdaptivePalette { get; }

    public IReadOnlySet<uint> AdaptivePalettePictures { get; }

    /// <summary>Gets whether the Blorb contains the V3-only Loop chunk.</summary>
    public bool HasVersion3SoundLooping { get; }

    public IReadOnlyDictionary<uint, uint> Version3SoundRepeats { get; }
}

/// <summary>Parses a Blorb IFRS container and provides indexed access to its resources.</summary>
public sealed class BlorbFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictBigEndianUnicode = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private readonly IReadOnlyDictionary<(string Usage, uint Number), BlorbResource> _resourceLookup;

    private BlorbFile(
        byte[] data,
        IReadOnlyList<BlorbChunk> chunks,
        IReadOnlyList<BlorbResource> resources,
        BlorbMetadata metadata)
    {
        Data = data;
        Chunks = chunks;
        Resources = resources;
        Metadata = metadata;
        Executables = Array.AsReadOnly(resources.Where(resource => resource.Usage == "Exec").ToArray());
        _resourceLookup = new ReadOnlyDictionary<(string Usage, uint Number), BlorbResource>(
            resources
                .Where(resource => resource.Usage != "Exec")
                .GroupBy(resource => (resource.Usage, resource.Number))
                .ToDictionary(group => group.Key, group => group.First()));
    }

    /// <summary>Gets an immutable copy of the source Blorb bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public IReadOnlyList<BlorbChunk> Chunks { get; }

    public IReadOnlyList<BlorbResource> Resources { get; }

    public IReadOnlyList<BlorbResource> Executables { get; }

    public int PictureCount => Resources.Count(resource => resource.Usage == "Pict");

    public BlorbMetadata Metadata { get; }

    public static BlorbFile Parse(ReadOnlyMemory<byte> input)
    {
        if (input.Length < 12)
        {
            throw new StoryLoadException("The Blorb file is shorter than its 12-byte FORM header.");
        }

        var data = input.ToArray();
        if (!HasFourCc(data, 0, "FORM"))
        {
            throw new StoryLoadException("The file does not begin with the IFF FORM marker.");
        }

        var formLength = ReadUInt32(data, 4);
        var formEnd = 8L + formLength;
        if (formLength < 4 || formEnd != data.Length)
        {
            throw new StoryLoadException($"Invalid Blorb FORM length {formLength}; the FORM must end at the end of the file.");
        }

        if (!HasFourCc(data, 8, "IFRS"))
        {
            var formType = ReadFourCc(data, 8, "FORM type");
            throw new StoryLoadException($"Unsupported FORM type '{formType}'; expected an IFRS Blorb file.");
        }

        var chunks = new List<BlorbChunk>();
        var chunksByOffset = new Dictionary<int, BlorbChunk>();
        var chunkIds = new List<string>();
        var cursor = 12;
        BlorbChunk? resourceIndex = null;
        while (cursor < formEnd)
        {
            if (formEnd - cursor < 8)
            {
                throw new StoryLoadException($"Blorb chunk header at offset 0x{cursor:x} is truncated.");
            }

            var id = ReadFourCc(data, cursor, "chunk ID");
            var length = ReadUInt32(data, cursor + 4);
            var contentOffset = cursor + 8L;
            var end = contentOffset + length;
            var paddedEnd = end + (length & 1);
            if (end > formEnd || paddedEnd > formEnd)
            {
                throw new StoryLoadException($"Blorb chunk '{id}' at offset 0x{cursor:x} extends beyond the IFRS FORM.");
            }

            if ((length & 1) != 0 && data[checked((int)end)] != 0)
            {
                throw new StoryLoadException($"Blorb chunk '{id}' at offset 0x{cursor:x} has a nonzero IFF pad byte.");
            }

            var chunk = new BlorbChunk(id, cursor, data.AsMemory(checked((int)contentOffset), checked((int)length)));
            chunks.Add(chunk);
            chunksByOffset.Add(cursor, chunk);
            chunkIds.Add(id);
            if (id == "RIdx")
            {
                if (resourceIndex is not null)
                {
                    throw new StoryLoadException("The Blorb file contains more than one RIdx resource index.");
                }

                if (chunks.Count != 1)
                {
                    throw new StoryLoadException("The RIdx resource index must be the first chunk in a Blorb file.");
                }

                resourceIndex = chunk;
            }

            cursor = checked((int)paddedEnd);
        }

        if (cursor != formEnd)
        {
            throw new StoryLoadException("The Blorb chunks do not end at the declared FORM boundary.");
        }

        if (resourceIndex is null)
        {
            throw new StoryLoadException("The Blorb file has no RIdx resource index.");
        }

        var indexedEntries = ReadResourceIndex(resourceIndex);
        var resources = new List<BlorbResource>(indexedEntries.Count);
        var resourcePairs = new HashSet<(string Usage, uint Number)>();
        var previousOffset = -1;
        foreach (var entry in indexedEntries)
        {
            if (!chunksByOffset.TryGetValue(entry.Offset, out var chunk))
            {
                throw new StoryLoadException($"Blorb resource '{entry.Usage}' number {entry.Number} refers to no chunk at offset 0x{entry.Offset:x}.");
            }

            if (chunk.Id == "RIdx")
            {
                throw new StoryLoadException("A Blorb resource index entry may not refer to the RIdx chunk itself.");
            }

            if (entry.Offset < previousOffset)
            {
                throw new StoryLoadException("Blorb RIdx entries are not in the same order as their resource chunks.");
            }

            if (entry.Usage == "Exec" && entry.Number != 0)
            {
                throw new StoryLoadException($"Blorb executable resource number {entry.Number} is invalid; Exec resources must use number 0.");
            }

            if (entry.Usage != "Exec" && !resourcePairs.Add((entry.Usage, entry.Number)))
            {
                throw new StoryLoadException($"Blorb contains duplicate resource index entries for '{entry.Usage}' number {entry.Number}.");
            }

            ValidateResourceChunkType(entry.Usage, entry.Number, chunk);
            resources.Add(new BlorbResource(entry.Usage, entry.Number, chunk));
            previousOffset = entry.Offset;
        }

        var metadata = ParseMetadata(chunks, resources, chunkIds);
        return new BlorbFile(data, Array.AsReadOnly(chunks.ToArray()), Array.AsReadOnly(resources.ToArray()), metadata);
    }

    public BlorbResource? GetResource(string usage, uint number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        return _resourceLookup.GetValueOrDefault((usage, number));
    }

    /// <summary>Gets the parsed dimensions and metadata for a picture resource, or null when it is unavailable.</summary>
    public BlorbPicture? GetPicture(uint number)
    {
        var resource = GetResource("Pict", number);
        return resource is null ? null : BlorbPicture.Create(resource, Metadata);
    }

    /// <summary>Gets the picture designated by the optional frontispiece chunk, when present.</summary>
    public BlorbPicture? GetFrontispiece() => Metadata.FrontispieceNumber is { } number ? GetPicture(number) : null;

    /// <summary>Gets picture dimensions after applying any Blorb Reso scaling hints.</summary>
    public BlorbPictureSize? GetPictureSize(uint number, uint windowWidth, uint windowHeight)
    {
        var picture = GetPicture(number);
        return picture?.GetDisplaySize(Metadata, windowWidth, windowHeight);
    }

    /// <summary>Selects the first indexed ZCOD executable, or reports why none is usable.</summary>
    public BlorbResource SelectZCodeExecutable()
    {
        if (Executables.Count == 0)
        {
            throw new StoryLoadException("The Blorb file contains no embedded executable story.");
        }

        return Executables.FirstOrDefault(resource => resource.Chunk.Id == "ZCOD")
            ?? throw new StoryLoadException("The Blorb file contains no ZCOD Z-code executable resource.");
    }

    /// <summary>Checks a Z-code IFhd chunk against the embedded executable, when present.</summary>
    public void ValidateZCodeIdentity(ReadOnlySpan<byte> story)
    {
        if (Metadata.GameIdentifier is not { } identifier)
        {
            return;
        }

        if (identifier.Length != 13)
        {
            throw new StoryLoadException($"The Blorb IFhd game identifier has {identifier.Length} bytes; a Z-code identifier must have 13.");
        }

        var id = identifier.Span;
        if (id[10] != 0 || id[11] != 0 || id[12] != 0)
        {
            throw new StoryLoadException("The Blorb IFhd Z-code initial-PC field must be zero.");
        }

        if (story.Length < 0x1e || !id[..2].SequenceEqual(story.Slice(0x02, 2)) ||
            !id.Slice(2, 6).SequenceEqual(story.Slice(0x12, 6)) ||
            !id.Slice(8, 2).SequenceEqual(story.Slice(0x1c, 2)))
        {
            throw new StoryLoadException("The Blorb IFhd game identifier does not match its Z-code executable.");
        }
    }

    private static IReadOnlyList<IndexedEntry> ReadResourceIndex(BlorbChunk chunk)
    {
        var data = chunk.Data.Span;
        if (data.Length < 4)
        {
            throw new StoryLoadException("The Blorb RIdx chunk is too short to contain a resource count.");
        }

        var count = ReadUInt32(data, 0);
        var expectedLength = 4L + (count * 12L);
        if (expectedLength != data.Length)
        {
            throw new StoryLoadException($"The Blorb RIdx chunk length is {data.Length}, but its {count} entries require {expectedLength} bytes.");
        }

        var entries = new IndexedEntry[checked((int)count)];
        for (var index = 0; index < entries.Length; index++)
        {
            var offset = 4 + (index * 12);
            var usage = ReadFourCc(data, offset, "resource usage");
            var number = ReadUInt32(data, offset + 4);
            var chunkOffset = ReadUInt32(data, offset + 8);
            if (chunkOffset > int.MaxValue)
            {
                throw new StoryLoadException($"Blorb resource '{usage}' number {number} has an out-of-range chunk offset.");
            }

            entries[index] = new IndexedEntry(usage, number, (int)chunkOffset);
        }

        return entries;
    }

    private static BlorbMetadata ParseMetadata(IReadOnlyList<BlorbChunk> chunks, IReadOnlyList<BlorbResource> resources, IReadOnlyList<string> chunkIds)
    {
        ValidateUniqueChunks(chunkIds);
        ushort? releaseNumber = null;
        ReadOnlyMemory<byte>? gameIdentifier = null;
        string? author = null;
        string? copyright = null;
        string? annotation = null;
        string? legacyStoryName = null;
        ReadOnlyMemory<byte>? xml = null;
        uint? frontispiece = null;
        byte? preferredColorDepth = null;
        var palette = new List<BlorbColor>();
        var descriptions = new List<BlorbResourceDescription>();
        var descriptionKeys = new HashSet<(string Usage, uint Number)>();
        uint? standardWidth = null;
        uint? standardHeight = null;
        uint? minimumWidth = null;
        uint? minimumHeight = null;
        uint? maximumWidth = null;
        uint? maximumHeight = null;
        var resolutions = new List<BlorbResolutionEntry>();
        var adaptivePictures = new HashSet<uint>();
        var looping = new Dictionary<uint, uint>();

        foreach (var chunk in chunks)
        {
            var data = chunk.Data.Span;
            switch (chunk.Id)
            {
                case "RelN":
                    RequireLength(chunk, 2);
                    releaseNumber = ReadUInt16(data, 0);
                    break;
                case "IFhd":
                    gameIdentifier = chunk.Data;
                    break;
                case "AUTH":
                    author = ReadAsciiText(chunk);
                    break;
                case "(c) ":
                    copyright = ReadAsciiText(chunk);
                    break;
                case "ANNO":
                    annotation = ReadAsciiText(chunk);
                    break;
                case "SNam":
                    if ((data.Length & 1) != 0)
                    {
                        throw new StoryLoadException("The deprecated Blorb SNam story-name chunk must contain an even number of bytes.");
                    }

                    try
                    {
                        legacyStoryName = StrictBigEndianUnicode.GetString(data);
                    }
                    catch (DecoderFallbackException exception)
                    {
                        throw new StoryLoadException("The deprecated Blorb SNam story-name chunk contains invalid UTF-16BE text.", exception);
                    }

                    break;
                case "IFmd":
                    ValidateUtf8(chunk, "IFmd metadata");
                    xml = chunk.Data;
                    break;
                case "Plte":
                    ParsePalette(chunk, data, palette, ref preferredColorDepth);
                    break;
                case "Fspc":
                    RequireLength(chunk, 4);
                    frontispiece = ReadUInt32(data, 0);
                    break;
                case "RDes":
                    ParseDescriptions(chunk, data, descriptions, descriptionKeys);
                    break;
                case "Reso":
                    ParseResolutions(chunk, data, resolutions, ref standardWidth, ref standardHeight, ref minimumWidth, ref minimumHeight, ref maximumWidth, ref maximumHeight);
                    break;
                case "APal":
                    if ((data.Length & 3) != 0)
                    {
                        throw new StoryLoadException($"The Blorb APal chunk length {data.Length} is not a multiple of four bytes.");
                    }

                    for (var offset = 0; offset < data.Length; offset += 4)
                    {
                        if (!adaptivePictures.Add(ReadUInt32(data, offset)))
                        {
                            throw new StoryLoadException("The Blorb APal chunk contains a duplicate picture number.");
                        }
                    }

                    break;
                case "Loop":
                    ParseLooping(chunk, data, looping);
                    break;
            }
        }

        if (chunkIds.Contains("Plte") && !resources.Any(resource => resource.Usage == "Pict"))
        {
            throw new StoryLoadException("The Blorb Plte palette chunk is present without any picture resources.");
        }

        var hasAdaptivePalette = chunkIds.Contains("APal");
        if (hasAdaptivePalette && resources.Any(resource => resource.Usage == "Pict" && resource.Chunk.Id is not ("PNG " or "Rect")))
        {
            throw new StoryLoadException("The Blorb APal chunk requires every picture resource to use PNG or Rect format.");
        }

        ValidatePictureReferences("Fspc", frontispiece, resources, requireNonRectangle: true);
        foreach (var number in adaptivePictures)
        {
            ValidatePictureReferences("APal", number, resources, requireNonRectangle: false);
        }

        foreach (var description in descriptions)
        {
            if (description.Usage is "Pict" or "Snd " && !resources.Any(resource => resource.Usage == description.Usage && resource.Number == description.Number))
            {
                throw new StoryLoadException($"The Blorb RDes chunk describes missing '{description.Usage}' resource {description.Number}.");
            }
        }

        var seenResolutionNumbers = new HashSet<uint>();
        foreach (var resolution in resolutions)
        {
            if (!seenResolutionNumbers.Add(resolution.PictureNumber))
            {
                throw new StoryLoadException($"The Blorb Reso chunk contains duplicate entries for picture {resolution.PictureNumber}.");
            }

            ValidatePictureReferences("Reso", resolution.PictureNumber, resources, requireNonRectangle: false);
        }

        foreach (var soundNumber in looping.Keys.Where(number => number >= 3))
        {
            if (!resources.Any(resource => resource.Usage == "Snd " && resource.Number == soundNumber))
            {
                throw new StoryLoadException($"The Blorb Loop chunk refers to missing sound resource {soundNumber}.");
            }
        }

        return new BlorbMetadata(
            releaseNumber,
            gameIdentifier,
            author,
            copyright,
            annotation,
            legacyStoryName,
            xml,
            frontispiece,
            preferredColorDepth,
            Array.AsReadOnly(palette.ToArray()),
            Array.AsReadOnly(descriptions.ToArray()),
            standardWidth,
            standardHeight,
            minimumWidth,
            minimumHeight,
            maximumWidth,
            maximumHeight,
            Array.AsReadOnly(resolutions.ToArray()),
            hasAdaptivePalette,
            new ReadOnlySet<uint>(adaptivePictures),
            chunkIds.Contains("Loop"),
            new ReadOnlyDictionary<uint, uint>(looping));
    }

    private static void ValidateUniqueChunks(IReadOnlyList<string> chunkIds)
    {
        var uniqueIds = new[] { "RelN", "IFhd", "Plte", "Fspc", "Reso", "APal", "Loop", "AUTH", "(c) ", "ANNO", "SNam" };
        foreach (var id in uniqueIds)
        {
            if (chunkIds.Count(candidate => candidate == id) > 1)
            {
                throw new StoryLoadException($"The Blorb file contains more than one '{id}' chunk.");
            }
        }
    }

    private static void ValidateResourceChunkType(string usage, uint number, BlorbChunk chunk)
    {
        if (usage == "Pict" && number == 0)
        {
            throw new StoryLoadException("Blorb picture resources are numbered from 1; picture number 0 is reserved by picture_data.");
        }

        var valid = usage switch
        {
            "Pict" => chunk.Id is "PNG " or "JPEG" or "Rect",
            "Snd " => chunk.Id is "OGGV" or "MOD " or "SONG" || IsAiffForm(chunk),
            "Data" => chunk.Id is "TEXT" or "BINA",
            "Exec" => true,
            _ => true
        };
        if (!valid)
        {
            throw new StoryLoadException($"Blorb resource '{usage}' number {number} points to incompatible chunk type '{chunk.Id}'.");
        }

        if (usage == "Pict" && chunk.Id == "Rect" && chunk.Data.Length != 8)
        {
            throw new StoryLoadException($"Blorb Rect picture {number} has length {chunk.Data.Length}; placeholder rectangles require eight bytes.");
        }
    }

    private static bool IsAiffForm(BlorbChunk chunk)
    {
        var data = chunk.Data.Span;
        if (chunk.Id != "FORM" || data.Length < 4 || !data[..4].SequenceEqual("AIFF"u8))
        {
            return false;
        }

        if (data.Length < 12)
        {
            throw new StoryLoadException("A Blorb AIFF sound resource has a truncated nested FORM header.");
        }

        var nestedLength = ReadUInt32(data, 4);
        if (nestedLength < 4 || 8L + nestedLength != data.Length)
        {
            throw new StoryLoadException("A Blorb AIFF sound resource has an invalid nested FORM length.");
        }

        return true;
    }

    private static void ParsePalette(BlorbChunk chunk, ReadOnlySpan<byte> data, List<BlorbColor> palette, ref byte? preferredColorDepth)
    {
        if (data.Length == 1)
        {
            if (data[0] is not (16 or 32))
            {
                throw new StoryLoadException($"The Blorb Plte direct-color hint {data[0]} must be 16 or 32.");
            }

            preferredColorDepth = data[0];
            return;
        }

        if (data.Length == 0 || data.Length % 3 != 0 || data.Length > 768)
        {
            throw new StoryLoadException($"The Blorb Plte chunk length {data.Length} must be one byte or a multiple of three from 3 through 768.");
        }

        for (var offset = 0; offset < data.Length; offset += 3)
        {
            palette.Add(new BlorbColor(data[offset], data[offset + 1], data[offset + 2]));
        }
    }

    private static void ParseDescriptions(
        BlorbChunk chunk,
        ReadOnlySpan<byte> data,
        List<BlorbResourceDescription> descriptions,
        HashSet<(string Usage, uint Number)> keys)
    {
        if (data.Length < 4)
        {
            throw new StoryLoadException("The Blorb RDes chunk is too short to contain an entry count.");
        }

        var count = ReadUInt32(data, 0);
        var cursor = 4;
        for (uint index = 0; index < count; index++)
        {
            if (data.Length - cursor < 12)
            {
                throw new StoryLoadException("The Blorb RDes chunk ends before a resource description header is complete.");
            }

            var usage = ReadFourCc(data, cursor, "RDes resource usage");
            var number = ReadUInt32(data, cursor + 4);
            var textLength = ReadUInt32(data, cursor + 8);
            cursor += 12;
            if (textLength > data.Length - cursor)
            {
                throw new StoryLoadException("The Blorb RDes description text extends beyond its chunk.");
            }

            if (!keys.Add((usage, number)))
            {
                throw new StoryLoadException($"The Blorb RDes chunk contains duplicate descriptions for '{usage}' resource {number}.");
            }

            try
            {
                var text = StrictUtf8.GetString(data.Slice(cursor, checked((int)textLength)));
                descriptions.Add(new BlorbResourceDescription(usage, number, text));
            }
            catch (DecoderFallbackException exception)
            {
                throw new StoryLoadException("The Blorb RDes chunk contains invalid UTF-8 description text.", exception);
            }

            cursor += checked((int)textLength);
        }

        if (cursor != data.Length)
        {
            throw new StoryLoadException("The Blorb RDes chunk contains trailing bytes after its declared descriptions.");
        }
    }

    private static void ParseResolutions(
        BlorbChunk chunk,
        ReadOnlySpan<byte> data,
        List<BlorbResolutionEntry> resolutions,
        ref uint? standardWidth,
        ref uint? standardHeight,
        ref uint? minimumWidth,
        ref uint? minimumHeight,
        ref uint? maximumWidth,
        ref uint? maximumHeight)
    {
        if (data.Length < 24 || (data.Length - 24) % 28 != 0)
        {
            throw new StoryLoadException($"The Blorb Reso chunk length {data.Length} must be 24 plus a multiple of 28 bytes.");
        }

        standardWidth = ReadUInt32(data, 0);
        standardHeight = ReadUInt32(data, 4);
        minimumWidth = ReadUInt32(data, 8);
        minimumHeight = ReadUInt32(data, 12);
        maximumWidth = ReadUInt32(data, 16);
        maximumHeight = ReadUInt32(data, 20);
        if (standardWidth == 0 || standardHeight == 0 ||
            (minimumWidth != 0 && (minimumWidth > standardWidth || (maximumWidth != 0 && minimumWidth > maximumWidth))) ||
            (minimumHeight != 0 && (minimumHeight > standardHeight || (maximumHeight != 0 && minimumHeight > maximumHeight))) ||
            (maximumWidth != 0 && maximumWidth < standardWidth) ||
            (maximumHeight != 0 && maximumHeight < standardHeight))
        {
            throw new StoryLoadException("The Blorb Reso chunk has invalid standard, minimum, or maximum window dimensions.");
        }

        for (var offset = 24; offset < data.Length; offset += 28)
        {
            var entry = new BlorbResolutionEntry(
                ReadUInt32(data, offset),
                ReadUInt32(data, offset + 4),
                ReadUInt32(data, offset + 8),
                ReadUInt32(data, offset + 12),
                ReadUInt32(data, offset + 16),
                ReadUInt32(data, offset + 20),
                ReadUInt32(data, offset + 24));
            ValidateRatio(entry.StandardNumerator, entry.StandardDenominator, allowZeroPair: false, "standard");
            ValidateRatio(entry.MinimumNumerator, entry.MinimumDenominator, allowZeroPair: true, "minimum");
            ValidateRatio(entry.MaximumNumerator, entry.MaximumDenominator, allowZeroPair: true, "maximum");
            if (entry.MinimumNumerator != 0 && entry.MaximumNumerator != 0 &&
                (ulong)entry.MinimumNumerator * entry.MaximumDenominator > (ulong)entry.MaximumNumerator * entry.MinimumDenominator)
            {
                throw new StoryLoadException("The Blorb Reso minimum image ratio is greater than its maximum ratio.");
            }

            resolutions.Add(entry);
        }
    }

    private static void ValidateRatio(uint numerator, uint denominator, bool allowZeroPair, string name)
    {
        if (allowZeroPair && numerator == 0 && denominator == 0)
        {
            return;
        }

        if (numerator == 0 || denominator == 0)
        {
            throw new StoryLoadException($"The Blorb Reso {name} image ratio must have both numerator and denominator nonzero, or both zero where allowed.");
        }
    }

    private static void ParseLooping(BlorbChunk chunk, ReadOnlySpan<byte> data, Dictionary<uint, uint> looping)
    {
        if ((data.Length & 7) != 0)
        {
            throw new StoryLoadException($"The Blorb Loop chunk length {data.Length} is not a multiple of eight bytes.");
        }

        for (var offset = 0; offset < data.Length; offset += 8)
        {
            var number = ReadUInt32(data, offset);
            var repeats = ReadUInt32(data, offset + 4);
            if (repeats is not (0 or 1))
            {
                throw new StoryLoadException($"The Blorb Loop entry for sound {number} must have a repeats value of zero or one.");
            }

            if (!looping.TryAdd(number, repeats))
            {
                throw new StoryLoadException($"The Blorb Loop chunk contains duplicate entries for sound {number}.");
            }
        }
    }

    private static void ValidatePictureReferences(string chunkId, uint? number, IReadOnlyList<BlorbResource> resources, bool requireNonRectangle)
    {
        if (!number.HasValue)
        {
            return;
        }

        var picture = resources.FirstOrDefault(resource => resource.Usage == "Pict" && resource.Number == number.Value);
        if (picture is null)
        {
            throw new StoryLoadException($"The Blorb {chunkId} chunk refers to missing picture resource {number.Value}.");
        }

        if (requireNonRectangle && picture.Chunk.Id == "Rect")
        {
            throw new StoryLoadException("The Blorb Fspc frontispiece may not refer to a placeholder Rect picture.");
        }
    }

    private static string ReadAsciiText(BlorbChunk chunk)
    {
        foreach (var value in chunk.Data.Span)
        {
            if (value is < 0x20 or > 0x7e)
            {
                throw new StoryLoadException($"The Blorb {chunk.Id} text chunk contains a byte outside printable ASCII.");
            }
        }

        return Encoding.ASCII.GetString(chunk.Data.Span);
    }

    private static void ValidateUtf8(BlorbChunk chunk, string description)
    {
        try
        {
            _ = StrictUtf8.GetString(chunk.Data.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new StoryLoadException($"The Blorb {description} chunk contains invalid UTF-8.", exception);
        }
    }

    private static void RequireLength(BlorbChunk chunk, int expectedLength)
    {
        if (chunk.Data.Length != expectedLength)
        {
            throw new StoryLoadException($"The Blorb {chunk.Id} chunk has length {chunk.Data.Length}; expected {expectedLength} bytes.");
        }
    }

    private static bool HasFourCc(byte[] data, int offset, string value) =>
        offset >= 0 && offset <= data.Length - value.Length && data.AsSpan(offset, value.Length).SequenceEqual(Encoding.ASCII.GetBytes(value));

    private static string ReadFourCc(byte[] data, int offset, string description)
    {
        var fourCc = data.AsSpan(offset, 4);
        for (var index = 0; index < fourCc.Length; index++)
        {
            if (fourCc[index] is < 0x20 or > 0x7e)
            {
                throw new StoryLoadException($"The Blorb {description} at offset 0x{offset:x} is not printable ASCII.");
            }
        }

        return Encoding.ASCII.GetString(fourCc);
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset, string description)
    {
        var fourCc = data.Slice(offset, 4);
        for (var index = 0; index < fourCc.Length; index++)
        {
            if (fourCc[index] is < 0x20 or > 0x7e)
            {
                throw new StoryLoadException($"The Blorb {description} at byte {offset} is not printable ASCII.");
            }
        }

        return Encoding.ASCII.GetString(fourCc);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private sealed record IndexedEntry(string Usage, uint Number, int Offset);

    private sealed class ReadOnlySet<T>(ISet<T> values) : IReadOnlySet<T>
    {
        public int Count => values.Count;
        public bool Contains(T item) => values.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<T> other) => values.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => values.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => values.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => values.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => values.Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => values.SetEquals(other);
        public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
