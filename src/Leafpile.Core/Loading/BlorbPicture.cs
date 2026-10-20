using System.Buffers.Binary;

namespace Leafpile.Core.Loading;

public enum BlorbPictureFormat
{
    Png,
    Jpeg,
    Rectangle
}

public readonly record struct BlorbPictureSize(uint Width, uint Height);

/// <summary>Describes one indexed picture resource without tying the core to a UI renderer.</summary>
public sealed class BlorbPicture
{
    private BlorbPicture(
        uint number,
        BlorbPictureFormat format,
        uint width,
        uint height,
        ReadOnlyMemory<byte> data,
        IReadOnlyList<BlorbColor> palette,
        bool adaptivePalette,
        string? description)
    {
        Number = number;
        Format = format;
        Width = width;
        Height = height;
        Data = data;
        Palette = palette;
        UsesAdaptivePalette = adaptivePalette;
        Description = description;
    }

    public uint Number { get; }

    public BlorbPictureFormat Format { get; }

    public uint Width { get; }

    public uint Height { get; }

    /// <summary>Gets the original PNG or JPEG bytes. Rect resources contain their eight-byte size record.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the PNG PLTE entries, when present.</summary>
    public IReadOnlyList<BlorbColor> Palette { get; }

    public bool UsesAdaptivePalette { get; }

    public string? Description { get; }

    /// <summary>Applies the picture's Reso scale and rounds each reported dimension to the nearest pixel.</summary>
    public BlorbPictureSize GetDisplaySize(BlorbMetadata metadata, uint windowWidth, uint windowHeight)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var resolution = metadata.Resolutions.FirstOrDefault(entry => entry.PictureNumber == Number);
        if (resolution is null)
        {
            return new BlorbPictureSize(Width, Height);
        }

        var standardWidth = metadata.StandardWindowWidth!.Value;
        var standardHeight = metadata.StandardWindowHeight!.Value;
        var widthFactor = (double)windowWidth / standardWidth;
        var heightFactor = (double)windowHeight / standardHeight;
        var elbowRoom = Math.Min(widthFactor, heightFactor);
        var scale = elbowRoom * resolution.StandardNumerator / resolution.StandardDenominator;
        if (resolution.MinimumNumerator != 0)
        {
            scale = Math.Max(scale, (double)resolution.MinimumNumerator / resolution.MinimumDenominator);
        }

        if (resolution.MaximumNumerator != 0)
        {
            scale = Math.Min(scale, (double)resolution.MaximumNumerator / resolution.MaximumDenominator);
        }

        return new BlorbPictureSize(ScaleDimension(Width, scale), ScaleDimension(Height, scale));
    }

    internal static BlorbPicture Create(BlorbResource resource, BlorbMetadata metadata)
    {
        uint width;
        uint height;
        BlorbPictureFormat format;
        IReadOnlyList<BlorbColor> palette = Array.Empty<BlorbColor>();
        byte colorType = 0;
        switch (resource.Chunk.Id)
        {
            case "PNG ":
                (width, height, palette, colorType) = ReadPng(resource.Chunk.Data.Span);
                format = BlorbPictureFormat.Png;
                if (metadata.HasAdaptivePalette && (colorType != 3 || palette.Count > 16))
                {
                    throw new StoryLoadException($"Blorb PNG picture {resource.Number} violates the indexed-color limits required by APal.");
                }
                break;
            case "JPEG":
                (width, height) = ReadJpeg(resource.Chunk.Data.Span);
                format = BlorbPictureFormat.Jpeg;
                break;
            case "Rect":
                width = BinaryPrimitives.ReadUInt32BigEndian(resource.Chunk.Data.Span[..4]);
                height = BinaryPrimitives.ReadUInt32BigEndian(resource.Chunk.Data.Span.Slice(4, 4));
                format = BlorbPictureFormat.Rectangle;
                break;
            default:
                throw new StoryLoadException($"Picture resource {resource.Number} has unsupported chunk type '{resource.Chunk.Id}'.");
        }

        var description = metadata.ResourceDescriptions.FirstOrDefault(item => item.Usage == "Pict" && item.Number == resource.Number)?.Text;
        var adaptive = metadata.AdaptivePalettePictures.Contains(resource.Number);
        return new BlorbPicture(resource.Number, format, width, height, resource.Chunk.Data, palette, adaptive, description);
    }

    private static (uint Width, uint Height, IReadOnlyList<BlorbColor> Palette, byte ColorType) ReadPng(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 33 || !data[..8].SequenceEqual(signature))
        {
            throw new StoryLoadException("A Blorb PNG picture has a missing signature or truncated IHDR chunk.");
        }

        var firstLength = ReadUInt32(data, 8);
        if (firstLength != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new StoryLoadException("A Blorb PNG picture must begin with a 13-byte IHDR chunk.");
        }

        var width = ReadUInt32(data, 16);
        var height = ReadUInt32(data, 20);
        var bitDepth = data[24];
        var colorType = data[25];
        var validBitDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 or 6 => bitDepth is 8 or 16,
            _ => false
        };
        if (width == 0 || height == 0 || !validBitDepth || data[26] != 0 || data[27] != 0 || data[28] > 1)
        {
            throw new StoryLoadException("A Blorb PNG picture has invalid IHDR dimensions, bit depth, color type, or compression fields.");
        }

        var palette = new List<BlorbColor>();
        var cursor = 8;
        var seenHeader = false;
        var seenPalette = false;
        var seenImageData = false;
        ulong imageDataLength = 0;
        var imageDataEnded = false;
        var seenEnd = false;
        while (cursor < data.Length)
        {
            if (data.Length - cursor < 12)
            {
                throw new StoryLoadException("A Blorb PNG picture has a truncated chunk header or checksum.");
            }

            var length = ReadUInt32(data, cursor);
            if (length > data.Length - cursor - 12)
            {
                throw new StoryLoadException("A Blorb PNG picture chunk extends beyond its resource data.");
            }

            var id = data.Slice(cursor + 4, 4);
            var chunkData = data.Slice(cursor + 8, checked((int)length));
            var isImageData = id.SequenceEqual("IDAT"u8);
            if (seenImageData && !isImageData && !id.SequenceEqual("IEND"u8))
            {
                imageDataEnded = true;
            }

            if (isImageData)
            {
                if (imageDataEnded)
                {
                    throw new StoryLoadException("A Blorb PNG picture has non-consecutive IDAT chunks.");
                }

                seenImageData = true;
                imageDataLength += length;
            }

            if (!seenHeader)
            {
                if (!id.SequenceEqual("IHDR"u8))
                {
                    throw new StoryLoadException("A Blorb PNG picture does not begin with IHDR.");
                }

                seenHeader = true;
            }
            else if (id.SequenceEqual("IHDR"u8))
            {
                throw new StoryLoadException("A Blorb PNG picture contains more than one IHDR chunk.");
            }

            if (id.SequenceEqual("PLTE"u8))
            {
                if (seenImageData || seenPalette || length is < 3 or > 768 || length % 3 != 0 || colorType is 0 or 4)
                {
                    throw new StoryLoadException("A Blorb PNG picture has an invalid or duplicate PLTE chunk.");
                }

                var maximumEntries = colorType == 3 ? 1 << bitDepth : 256;
                if (length / 3 > maximumEntries)
                {
                    throw new StoryLoadException("A Blorb PNG palette has more entries than its bit depth permits.");
                }

                for (var offset = 0; offset < chunkData.Length; offset += 3)
                {
                    palette.Add(new BlorbColor(chunkData[offset], chunkData[offset + 1], chunkData[offset + 2]));
                }

                seenPalette = true;
            }

            cursor = checked(cursor + 12 + (int)length);
            if (id.SequenceEqual("IEND"u8))
            {
                if (length != 0 || cursor != data.Length || !seenImageData)
                {
                    throw new StoryLoadException("A Blorb PNG picture has an invalid IEND chunk, no image data, or trailing bytes.");
                }

                seenEnd = true;
                break;
            }
        }

        if (!seenEnd || !seenImageData || imageDataLength == 0 || (colorType == 3 && !seenPalette))
        {
            throw new StoryLoadException("A Blorb PNG picture is missing IEND or its required indexed-color PLTE chunk.");
        }

        return (width, height, Array.AsReadOnly(palette.ToArray()), colorType);
    }

    private static (uint Width, uint Height) ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
        {
            throw new StoryLoadException("A Blorb JPEG picture has no SOI marker or is truncated.");
        }

        var cursor = 2;
        while (cursor < data.Length)
        {
            if (data[cursor++] != 0xff)
            {
                throw new StoryLoadException("A Blorb JPEG picture contains an invalid marker boundary.");
            }

            while (cursor < data.Length && data[cursor] == 0xff)
            {
                cursor++;
            }

            if (cursor >= data.Length)
            {
                break;
            }

            var marker = data[cursor++];
            if (marker is 0x01 or >= 0xd0 and <= 0xd9)
            {
                continue;
            }

            if (data.Length - cursor < 2)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor, 2));
            if (segmentLength < 2 || segmentLength > data.Length - cursor)
            {
                throw new StoryLoadException("A Blorb JPEG picture has an invalid segment length.");
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 8)
                {
                    throw new StoryLoadException("A Blorb JPEG frame header is too short to contain image dimensions.");
                }

                var components = data[cursor + 7];
                if (components == 0 || segmentLength != 8 + (components * 3))
                {
                    throw new StoryLoadException("A Blorb JPEG frame header has an invalid component count or length.");
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor + 5, 2));
                if (width == 0 || height == 0)
                {
                    throw new StoryLoadException("A Blorb JPEG frame has zero width or height.");
                }

                return (width, height);
            }

            cursor += segmentLength;
            if (marker == 0xda)
            {
                break;
            }
        }

        throw new StoryLoadException("A Blorb JPEG picture has no supported start-of-frame marker.");
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static uint ScaleDimension(uint dimension, double ratio)
    {
        var scaled = Math.Round(dimension * ratio, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(scaled) || scaled > uint.MaxValue)
        {
            throw new StoryLoadException("Blorb picture scaling produced a dimension outside the supported range.");
        }

        return checked((uint)Math.Max(0, scaled));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
