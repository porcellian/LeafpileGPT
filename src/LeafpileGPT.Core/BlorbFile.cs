namespace LeafpileGPT.Core;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

/// <summary>A parsed IFF Blorb resource file containing an IFRS form.</summary>
public sealed class BlorbFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private BlorbFile(
        ImmutableArray<BlorbChunk> chunks,
        ImmutableArray<BlorbResource> resources,
        ImmutableArray<BlorbResourceDescription> resourceDescriptions,
        ImmutableArray<byte> gameIdentifier,
        ImmutableArray<byte> metadata,
        long sourceFileSizeBytes,
        string sourceFileSha256,
        ushort releaseNumber,
        uint? frontispieceNumber,
        BlorbPictureCatalog pictureCatalog,
        BlorbSoundCatalog soundCatalog,
        StoryFile? zCodeStory)
    {
        Chunks = chunks;
        Resources = resources;
        ResourceDescriptions = resourceDescriptions;
        GameIdentifier = gameIdentifier;
        Metadata = metadata;
        SourceFileSizeBytes = sourceFileSizeBytes;
        SourceFileSha256 = sourceFileSha256;
        ReleaseNumber = releaseNumber;
        FrontispieceNumber = frontispieceNumber;
        Pictures = pictureCatalog;
        Sounds = soundCatalog;
        Executables = resources.Where(resource => resource.Usage == "Exec").ToImmutableArray();
        SelectedExecutable = Executables.IsDefaultOrEmpty ? null : Executables[0];
        ZCodeStory = zCodeStory;
    }

    /// <summary>Gets all chunks in file order, including unrecognized chunks.</summary>
    public ImmutableArray<BlorbChunk> Chunks { get; }

    /// <summary>Gets all resource-index entries in their specified order.</summary>
    public ImmutableArray<BlorbResource> Resources { get; }

    /// <summary>Gets all resource description entries in file order.</summary>
    public ImmutableArray<BlorbResourceDescription> ResourceDescriptions { get; }

    /// <summary>Gets the raw optional IFhd game identifier, or an empty array when absent.</summary>
    public ImmutableArray<byte> GameIdentifier { get; }

    /// <summary>Gets the raw optional UTF-8 IFmd document, or an empty array when absent.</summary>
    public ImmutableArray<byte> Metadata { get; }

    /// <summary>Gets the exact physical length of the parsed Blorb container.</summary>
    public long SourceFileSizeBytes { get; }

    /// <summary>Gets the SHA-256 fingerprint of the exact parsed Blorb container bytes.</summary>
    public string SourceFileSha256 { get; }

    /// <summary>Gets the optional Z-code release number; absent RelN chunks mean zero.</summary>
    public ushort ReleaseNumber { get; }

    /// <summary>Gets the optional frontispiece picture resource number.</summary>
    public uint? FrontispieceNumber { get; }

    /// <summary>Gets validated picture resources and display hints.</summary>
    public BlorbPictureCatalog Pictures { get; }

    /// <summary>Gets validated Blorb sound resources, channels, song samples, and V3 loop hints.</summary>
    public BlorbSoundCatalog Sounds { get; }

    /// <summary>Gets indexed executable resources in resource-index order.</summary>
    public ImmutableArray<BlorbResource> Executables { get; }

    /// <summary>Gets the preferred executable, which is the first Exec resource-index entry.</summary>
    public BlorbResource? SelectedExecutable { get; }

    /// <summary>Gets the selected executable as a validated Z-machine story when its chunk is ZCOD.</summary>
    public StoryFile? ZCodeStory { get; }

    /// <summary>Finds the first resource entry matching its case-sensitive FourCC usage and number.</summary>
    public BlorbResource? FindResource(string usage, uint number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usage);
        return Resources.FirstOrDefault(resource => resource.Usage == usage && resource.Number == number);
    }

    /// <summary>Finds the first chunk with a case-sensitive FourCC type.</summary>
    public BlorbChunk? FindChunk(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return Chunks.FirstOrDefault(chunk => chunk.Type == type);
    }

    public static BlorbFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static BlorbFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            throw new BlorbFormatException("Blorb FORM header is truncated; at least 12 bytes are required.");
        }

        if (ReadFourCc(data, 0) != "FORM")
        {
            throw new BlorbFormatException("Blorb file does not begin with the IFF FORM identifier.");
        }

        var formLength = ReadUInt32(data, 4);
        var declaredFileLength = (ulong)formLength + 8;
        if (declaredFileLength != (ulong)data.Length)
        {
            throw new BlorbFormatException(
                $"FORM declares {declaredFileLength} bytes, but the supplied file contains {data.Length} bytes.");
        }

        if (ReadFourCc(data, 8) != "IFRS")
        {
            throw new BlorbFormatException("IFF FORM type is not IFRS.");
        }

        var ownedBytes = data.ToArray();
        var chunks = ParseChunks(ownedBytes);
        ValidateResourceIndex(chunks, out var resources);
        var descriptions = ParseResourceDescriptions(chunks);
        var gameIdentifier = GetSingletonChunk(chunks, "IFhd")?.Data.ToArray().ToImmutableArray()
            ?? ImmutableArray<byte>.Empty;
        var metadataChunk = GetSingletonChunk(chunks, "IFmd");
        var metadata = metadataChunk is null
            ? ImmutableArray<byte>.Empty
            : ValidateMetadata(metadataChunk.Data.Span);
        var releaseChunk = GetSingletonChunk(chunks, "RelN");
        var release = releaseChunk is null
            ? (ushort)0
            : ReadExactWord(releaseChunk, "RelN");
        var frontispieceChunk = GetSingletonChunk(chunks, "Fspc");
        uint? frontispiece = frontispieceChunk is null
            ? null
            : ReadExactUInt32(frontispieceChunk, "Fspc");
        if (frontispiece is { } frontispieceResource
            && !resources.Any(resource => resource.Usage == "Pict" && resource.Number == frontispieceResource))
        {
            throw new BlorbFormatException($"Fspc refers to missing picture resource {frontispieceResource}.");
        }

        if (frontispiece is { } pictureNumber
            && resources.First(resource => resource.Usage == "Pict" && resource.Number == pictureNumber).Chunk.Type == "Rect")
        {
            throw new BlorbFormatException("Fspc cannot refer to a placeholder rectangle picture.");
        }

        var selectedExecutable = resources.FirstOrDefault(resource => resource.Usage == "Exec");
        StoryFile? zCodeStory = null;
        if (selectedExecutable?.Chunk.Type == "ZCOD")
        {
            try
            {
                zCodeStory = StoryFile.Parse(selectedExecutable.Data.Span);
            }
            catch (StoryFileFormatException exception)
            {
                throw new BlorbFormatException("The selected ZCOD executable is not a valid Z-machine story.", exception);
            }
        }

        var pictures = new BlorbPictureCatalog(resources, chunks, release, frontispiece);
        var sounds = new BlorbSoundCatalog(resources, chunks);

        return new BlorbFile(
            chunks,
            resources,
            descriptions,
            gameIdentifier,
            metadata,
            ownedBytes.Length,
            Convert.ToHexString(SHA256.HashData(ownedBytes)),
            release,
            frontispiece,
            pictures,
            sounds,
            zCodeStory);
    }

    private static ImmutableArray<BlorbChunk> ParseChunks(byte[] bytes)
    {
        var chunks = ImmutableArray.CreateBuilder<BlorbChunk>();
        var formEnd = bytes.Length;
        var cursor = 12;
        while (cursor < formEnd)
        {
            if (formEnd - cursor < 8)
            {
                throw new BlorbFormatException($"Chunk header at offset {cursor} is truncated.");
            }

            var type = ReadFourCc(bytes, cursor);
            var length = ReadUInt32(bytes, cursor + 4);
            var dataStart = (ulong)cursor + 8;
            var dataEnd = dataStart + length;
            var paddedEnd = dataEnd + (length & 1);
            if (paddedEnd > (ulong)formEnd)
            {
                throw new BlorbFormatException(
                    $"Chunk '{type}' at offset {cursor} declares {length} data bytes beyond the FORM boundary.");
            }

            if ((length & 1) != 0 && bytes[checked((int)dataEnd)] != 0)
            {
                throw new BlorbFormatException($"Chunk '{type}' at offset {cursor} has a non-zero IFF padding byte.");
            }

            chunks.Add(new BlorbChunk(
                type,
                checked((uint)cursor),
                bytes.AsMemory(checked((int)dataStart), checked((int)length))));
            cursor = checked((int)paddedEnd);
        }

        return chunks.ToImmutable();
    }

    private static void ValidateResourceIndex(
        ImmutableArray<BlorbChunk> chunks,
        out ImmutableArray<BlorbResource> resources)
    {
        if (chunks.IsDefaultOrEmpty || chunks[0].Type != "RIdx")
        {
            throw new BlorbFormatException("The first IFRS chunk must be the resource index (RIdx).");
        }

        var indexes = chunks.Where(chunk => chunk.Type == "RIdx").ToArray();
        if (indexes.Length != 1)
        {
            throw new BlorbFormatException($"An IFRS form must contain exactly one RIdx chunk; found {indexes.Length}.");
        }

        var index = indexes[0];
        if (index.Data.Length < sizeof(uint))
        {
            throw new BlorbFormatException("RIdx chunk is too short to contain its resource count.");
        }

        var count = ReadUInt32(index.Data.Span, 0);
        var expectedLength = sizeof(uint) + (ulong)count * 12;
        if (expectedLength != (ulong)index.Data.Length)
        {
            throw new BlorbFormatException(
                $"RIdx declares {count} entries requiring {expectedLength} bytes, but contains {index.Data.Length} bytes.");
        }

        var chunksByOffset = chunks.ToDictionary(chunk => chunk.Offset);
        var nonResourceChunkTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RIdx", "IFhd", "Plte", "Fspc", "RDes", "IFmd", "RelN", "Reso", "APal", "Loop", "AUTH", "(c) ", "ANNO", "SNam"
        };
        var resourceBuilder = ImmutableArray.CreateBuilder<BlorbResource>(checked((int)count));
        var indexedResources = new HashSet<(string Usage, uint Number)>();
        var cursor = sizeof(uint);
        for (var entry = 0u; entry < count; entry++)
        {
            var usage = ReadFourCc(index.Data.Span, cursor);
            var number = ReadUInt32(index.Data.Span, cursor + 4);
            var start = ReadUInt32(index.Data.Span, cursor + 8);
            if (!chunksByOffset.TryGetValue(start, out var chunk) || nonResourceChunkTypes.Contains(chunk.Type))
            {
                throw new BlorbFormatException(
                    $"RIdx resource {usage}:{number} points to offset {start}, which is not a resource chunk boundary.");
            }

            if (usage == "Exec" && number != 0)
            {
                throw new BlorbFormatException($"Exec resource number must be zero; found {number}.");
            }

            if (usage != "Exec" && !indexedResources.Add((usage, number)))
            {
                throw new BlorbFormatException($"RIdx contains duplicate resource {usage}:{number}.");
            }

            resourceBuilder.Add(new BlorbResource(usage, number, start, chunk));
            cursor += 12;
        }

        resources = resourceBuilder.ToImmutable();
    }

    private static ImmutableArray<BlorbResourceDescription> ParseResourceDescriptions(ImmutableArray<BlorbChunk> chunks)
    {
        var descriptions = ImmutableArray.CreateBuilder<BlorbResourceDescription>();
        var seenResources = new HashSet<(string Usage, uint Number)>();
        foreach (var chunk in chunks.Where(item => item.Type == "RDes"))
        {
            if (chunk.Data.Length < sizeof(uint))
            {
                throw new BlorbFormatException("RDes chunk is too short to contain its entry count.");
            }

            var count = ReadUInt32(chunk.Data.Span, 0);
            var cursor = sizeof(uint);
            for (var entry = 0u; entry < count; entry++)
            {
                if (chunk.Data.Length - cursor < 12)
                {
                    throw new BlorbFormatException($"RDes entry {entry} has a truncated fixed-size header.");
                }

                var usage = ReadFourCc(chunk.Data.Span, cursor);
                var number = ReadUInt32(chunk.Data.Span, cursor + 4);
                var textLength = ReadUInt32(chunk.Data.Span, cursor + 8);
                cursor += 12;
                if (textLength > (uint)(chunk.Data.Length - cursor))
                {
                    throw new BlorbFormatException($"RDes entry {usage}:{number} text extends beyond its chunk.");
                }

                if (!seenResources.Add((usage, number)))
                {
                    throw new BlorbFormatException($"RDes contains more than one description for {usage}:{number}.");
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(chunk.Data.Span.Slice(cursor, checked((int)textLength)));
                }
                catch (DecoderFallbackException exception)
                {
                    throw new BlorbFormatException($"RDes entry {usage}:{number} is not valid UTF-8.", exception);
                }

                descriptions.Add(new BlorbResourceDescription(usage, number, text));
                cursor += checked((int)textLength);
            }

            if (cursor != chunk.Data.Length)
            {
                throw new BlorbFormatException($"RDes has {chunk.Data.Length - cursor} unexpected trailing bytes.");
            }
        }

        return descriptions.ToImmutable();
    }

    private static ImmutableArray<byte> ValidateMetadata(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new BlorbFormatException("IFmd metadata is not valid UTF-8.", exception);
        }

        return bytes.ToArray().ToImmutableArray();
    }

    private static BlorbChunk? GetSingletonChunk(ImmutableArray<BlorbChunk> chunks, string type)
    {
        var matches = chunks.Where(chunk => chunk.Type == type).ToArray();
        if (matches.Length > 1)
        {
            throw new BlorbFormatException($"Blorb contains more than one '{type}' chunk.");
        }

        return matches.FirstOrDefault();
    }

    private static ushort ReadExactWord(BlorbChunk chunk, string name)
    {
        if (chunk.Data.Length != sizeof(ushort))
        {
            throw new BlorbFormatException($"{name} chunk must contain exactly two bytes.");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(chunk.Data.Span);
    }

    private static uint ReadExactUInt32(BlorbChunk chunk, string name)
    {
        if (chunk.Data.Length != sizeof(uint))
        {
            throw new BlorbFormatException($"{name} chunk must contain exactly four bytes.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(chunk.Data.Span);
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || data.Length - offset < 4)
        {
            throw new BlorbFormatException($"FourCC at offset {offset} is truncated.");
        }

        for (var index = 0; index < 4; index++)
        {
            if (data[offset + index] is < 0x20 or > 0x7E)
            {
                throw new BlorbFormatException($"FourCC at offset {offset} contains a non-printable ASCII byte.");
            }
        }

        return Encoding.ASCII.GetString(data.Slice(offset, 4));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || data.Length - offset < sizeof(uint))
        {
            throw new BlorbFormatException($"32-bit field at offset {offset} is truncated.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));
    }
}

/// <summary>An IFF chunk with a byte offset from the start of the FORM and its unpadded data.</summary>
public sealed record BlorbChunk(string Type, uint Offset, ReadOnlyMemory<byte> Data);

/// <summary>An RIdx entry linking a resource usage/number to its indexed chunk.</summary>
public sealed record BlorbResource(string Usage, uint Number, uint Start, BlorbChunk Chunk)
{
    public ReadOnlyMemory<byte> Data => Chunk.Data;
}

/// <summary>A UTF-8 alternative description for a resource.</summary>
public sealed record BlorbResourceDescription(string Usage, uint Number, string Text);

public sealed class BlorbFormatException : Exception
{
    public BlorbFormatException(string message)
        : base(message)
    {
    }

    public BlorbFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
