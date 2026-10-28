namespace LeafpileGPT.Core;

using System.Buffers.Binary;
using System.Collections.Immutable;

public enum BlorbPictureFormat
{
    Png,
    Jpeg,
    Rectangle
}

public readonly record struct BlorbColor(byte Red, byte Green, byte Blue);

public sealed record BlorbPictureResource(
    uint Number,
    BlorbPictureFormat Format,
    uint PixelWidth,
    uint PixelHeight,
    ReadOnlyMemory<byte> EncodedData,
    byte PngColorType,
    byte PngBitDepth,
    bool AdaptiveTransparencyValid,
    ImmutableArray<BlorbColor> Palette);

public sealed record BlorbPictureInfo(
    BlorbPictureResource Resource,
    int DisplayWidth,
    int DisplayHeight,
    bool UsesAdaptivePalette,
    ImmutableArray<BlorbColor> PaletteOverride);

public sealed record BlorbWindowSizeHints(
    uint StandardWidth,
    uint StandardHeight,
    uint MinimumWidth,
    uint MinimumHeight,
    uint MaximumWidth,
    uint MaximumHeight);

public sealed record BlorbImageScale(
    uint PictureNumber,
    uint StandardNumerator,
    uint StandardDenominator,
    uint MinimumNumerator,
    uint MinimumDenominator,
    uint MaximumNumerator,
    uint MaximumDenominator);

/// <summary>Provides validated picture resources, scaling hints, palettes, and frontispiece data.</summary>
public sealed class BlorbPictureCatalog
{
    private const int MaximumPngPaletteEntries = 256;
    private readonly ImmutableDictionary<uint, BlorbPictureResource> _pictures;
    private readonly ImmutableDictionary<uint, BlorbImageScale> _scales;
    private readonly ImmutableHashSet<uint> _adaptivePictures;
    private ImmutableArray<BlorbColor> _currentPalette = ImmutableArray<BlorbColor>.Empty;

    internal BlorbPictureCatalog(
        ImmutableArray<BlorbResource> resources,
        ImmutableArray<BlorbChunk> chunks,
        ushort releaseNumber,
        uint? frontispieceNumber)
    {
        ReleaseNumber = releaseNumber;
        FrontispieceNumber = frontispieceNumber;
        var palette = ParsePalette(chunks);
        DirectColorDepthHint = palette.DirectColorDepth;
        PaletteHint = palette.Colors;
        WindowSizeHints = ParseResolution(chunks, out var scales);
        _scales = scales;
        var adaptivePalette = ParseAdaptivePalette(chunks, resources);
        HasAdaptivePalette = adaptivePalette.Present;
        _adaptivePictures = adaptivePalette.Pictures;

        var pictures = ImmutableDictionary.CreateBuilder<uint, BlorbPictureResource>();
        foreach (var resource in resources.Where(item => item.Usage == "Pict"))
        {
            pictures.Add(resource.Number, ParsePicture(resource));
        }

        _pictures = pictures.ToImmutable();
        ValidateHintsReferencePictures();
        ValidateAdaptivePictures();
    }

    public int Count => _pictures.Count;

    public ushort ReleaseNumber { get; }

    public uint? FrontispieceNumber { get; }

    public byte? DirectColorDepthHint { get; }

    public ImmutableArray<BlorbColor> PaletteHint { get; }

    public BlorbWindowSizeHints? WindowSizeHints { get; }

    public ImmutableArray<BlorbImageScale> ImageScales => _scales.Values
        .OrderBy(scale => scale.PictureNumber)
        .ToImmutableArray();

    public ImmutableArray<uint> AdaptivePictureNumbers => _adaptivePictures.Order().ToImmutableArray();

    public bool HasAdaptivePalette { get; }

    public ImmutableArray<BlorbColor> CurrentPalette => _currentPalette;

    public bool TryGetPicture(uint number, uint windowWidth, uint windowHeight, out BlorbPictureInfo? picture)
    {
        if (!_pictures.TryGetValue(number, out var resource))
        {
            picture = null;
            return false;
        }

        var ratio = ResolveScale(number, windowWidth, windowHeight);
        var displayWidth = ScaleDimension(resource.PixelWidth, ratio);
        var displayHeight = ScaleDimension(resource.PixelHeight, ratio);
        var adaptive = _adaptivePictures.Contains(number);
        var paletteOverride = adaptive ? _currentPalette : ImmutableArray<BlorbColor>.Empty;
        picture = new BlorbPictureInfo(resource, displayWidth, displayHeight, adaptive, paletteOverride);
        return true;
    }

    /// <summary>Updates the adaptive palette after a non-adaptive picture is plotted.</summary>
    public ImmutableArray<BlorbColor> RecordPicturePlotted(uint number)
    {
        if (!_pictures.TryGetValue(number, out var picture))
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "The picture number is not present in this catalog.");
        }

        if (!_adaptivePictures.Contains(number) && !picture.Palette.IsDefaultOrEmpty)
        {
            var updated = _currentPalette.IsDefaultOrEmpty
                ? ImmutableArray.CreateRange(Enumerable.Repeat(default(BlorbColor), 16))
                : _currentPalette;
            var builder = updated.ToBuilder();
            for (var index = 0; index < Math.Min(picture.Palette.Length, 16); index++)
            {
                builder[index] = picture.Palette[index];
            }

            _currentPalette = builder.ToImmutable();
        }

        return _adaptivePictures.Contains(number) ? _currentPalette : ImmutableArray<BlorbColor>.Empty;
    }

    private double ResolveScale(uint number, uint windowWidth, uint windowHeight)
    {
        if (!_scales.TryGetValue(number, out var scale) || WindowSizeHints is not { } hints)
        {
            return 1d;
        }

        var widthRatio = windowWidth / (double)hints.StandardWidth;
        var heightRatio = windowHeight / (double)hints.StandardHeight;
        var elbowRoomFactor = Math.Min(widthRatio, heightRatio);
        var desired = elbowRoomFactor * scale.StandardNumerator / scale.StandardDenominator;
        var minimum = scale.MinimumNumerator == 0 ? 0d : scale.MinimumNumerator / (double)scale.MinimumDenominator;
        var maximum = scale.MaximumNumerator == 0 ? double.PositiveInfinity : scale.MaximumNumerator / (double)scale.MaximumDenominator;
        return Math.Clamp(desired, minimum, maximum);
    }

    private static int ScaleDimension(uint dimension, double ratio)
    {
        var scaled = Math.Round(dimension * ratio, MidpointRounding.AwayFromZero);
        return scaled >= int.MaxValue ? int.MaxValue : Math.Max(0, (int)scaled);
    }

    private void ValidateHintsReferencePictures()
    {
        foreach (var number in _scales.Keys)
        {
            if (!_pictures.ContainsKey(number))
            {
                throw new BlorbFormatException($"Reso contains scaling data for missing picture {number}.");
            }
        }
    }

    private void ValidateAdaptivePictures()
    {
        if (HasAdaptivePalette && _pictures.Values.Any(picture => picture.Format is not BlorbPictureFormat.Png and not BlorbPictureFormat.Rectangle))
        {
            throw new BlorbFormatException("APal requires every picture resource to be PNG or Rect.");
        }

        if (!HasAdaptivePalette)
        {
            return;
        }

        foreach (var picture in _pictures.Values.Where(item => item.Format == BlorbPictureFormat.Png))
        {
            if (picture.PngColorType != 3 || picture.Palette.Length > 16
                || picture.Palette.Length > 1 << picture.PngBitDepth || !picture.AdaptiveTransparencyValid)
            {
                throw new BlorbFormatException(
                    $"PNG picture {picture.Number} in an APal file must be indexed-color with a compatible palette and transparency chunk.");
            }
        }
    }

    private static (byte? DirectColorDepth, ImmutableArray<BlorbColor> Colors) ParsePalette(ImmutableArray<BlorbChunk> chunks)
    {
        var chunk = GetOptionalSingleton(chunks, "Plte");
        if (chunk is null)
        {
            return (null, ImmutableArray<BlorbColor>.Empty);
        }

        var data = chunk.Data.Span;
        if (data.Length == 1)
        {
            if (data[0] is not 16 and not 32)
            {
                throw new BlorbFormatException("Plte direct-color hint must be 16 or 32.");
            }

            return (data[0], ImmutableArray<BlorbColor>.Empty);
        }

        if (data.Length == 0 || data.Length % 3 != 0 || data.Length / 3 > MaximumPngPaletteEntries)
        {
            throw new BlorbFormatException("Plte must contain one direct-color byte or between 1 and 256 RGB triples.");
        }

        var colors = ImmutableArray.CreateBuilder<BlorbColor>(data.Length / 3);
        for (var offset = 0; offset < data.Length; offset += 3)
        {
            colors.Add(new BlorbColor(data[offset], data[offset + 1], data[offset + 2]));
        }

        return (null, colors.ToImmutable());
    }

    private BlorbWindowSizeHints? ParseResolution(ImmutableArray<BlorbChunk> chunks, out ImmutableDictionary<uint, BlorbImageScale> scales)
    {
        var chunk = GetOptionalSingleton(chunks, "Reso");
        if (chunk is null)
        {
            scales = ImmutableDictionary<uint, BlorbImageScale>.Empty;
            return null;
        }

        var data = chunk.Data.Span;
        if (data.Length < 24 || (data.Length - 24) % 28 != 0)
        {
            throw new BlorbFormatException("Reso length must be 24 bytes plus 28 bytes for each image entry.");
        }

        var standardWidth = ReadUInt32(data, 0);
        var standardHeight = ReadUInt32(data, 4);
        var minimumWidth = ReadUInt32(data, 8);
        var minimumHeight = ReadUInt32(data, 12);
        var maximumWidth = ReadUInt32(data, 16);
        var maximumHeight = ReadUInt32(data, 20);
        if (standardWidth == 0 || standardHeight == 0
            || minimumWidth != 0 && minimumWidth > standardWidth
            || minimumHeight != 0 && minimumHeight > standardHeight
            || maximumWidth != 0 && maximumWidth < standardWidth
            || maximumHeight != 0 && maximumHeight < standardHeight)
        {
            throw new BlorbFormatException("Reso standard dimensions must be positive and within any specified min/max bounds.");
        }

        var builder = ImmutableDictionary.CreateBuilder<uint, BlorbImageScale>();
        for (var offset = 24; offset < data.Length; offset += 28)
        {
            var scale = new BlorbImageScale(
                ReadUInt32(data, offset),
                ReadUInt32(data, offset + 4),
                ReadUInt32(data, offset + 8),
                ReadUInt32(data, offset + 12),
                ReadUInt32(data, offset + 16),
                ReadUInt32(data, offset + 20),
                ReadUInt32(data, offset + 24));
            ValidateRatio(scale.StandardNumerator, scale.StandardDenominator, allowNoLimit: false, "standard");
            ValidateRatio(scale.MinimumNumerator, scale.MinimumDenominator, allowNoLimit: true, "minimum");
            ValidateRatio(scale.MaximumNumerator, scale.MaximumDenominator, allowNoLimit: true, "maximum");
            if (scale.MinimumNumerator != 0 && scale.MaximumNumerator != 0
                && (ulong)scale.MinimumNumerator * scale.MaximumDenominator
                > (ulong)scale.MaximumNumerator * scale.MinimumDenominator)
            {
                throw new BlorbFormatException($"Reso picture {scale.PictureNumber} minimum scale exceeds its maximum.");
            }

            if (!builder.TryAdd(scale.PictureNumber, scale))
            {
                throw new BlorbFormatException($"Reso contains duplicate scaling data for picture {scale.PictureNumber}.");
            }
        }

        scales = builder.ToImmutable();
        return new BlorbWindowSizeHints(standardWidth, standardHeight, minimumWidth, minimumHeight, maximumWidth, maximumHeight);
    }

    private static (bool Present, ImmutableHashSet<uint> Pictures) ParseAdaptivePalette(
        ImmutableArray<BlorbChunk> chunks,
        ImmutableArray<BlorbResource> resources)
    {
        var chunk = GetOptionalSingleton(chunks, "APal");
        if (chunk is null)
        {
            return (false, ImmutableHashSet<uint>.Empty);
        }

        var data = chunk.Data.Span;
        if (data.Length % sizeof(uint) != 0)
        {
            throw new BlorbFormatException("APal length must contain a whole number of four-byte picture numbers.");
        }

        var knownPictures = resources.Where(resource => resource.Usage == "Pict").Select(resource => resource.Number).ToHashSet();
        var adaptive = ImmutableHashSet.CreateBuilder<uint>();
        for (var offset = 0; offset < data.Length; offset += sizeof(uint))
        {
            var number = ReadUInt32(data, offset);
            if (!knownPictures.Contains(number))
            {
                throw new BlorbFormatException($"APal refers to missing picture resource {number}.");
            }

            if (!adaptive.Add(number))
            {
                throw new BlorbFormatException($"APal contains duplicate picture resource {number}.");
            }
        }

        return (true, adaptive.ToImmutable());
    }

    private static BlorbPictureResource ParsePicture(BlorbResource resource)
    {
        var data = resource.Data.Span;
        return resource.Chunk.Type switch
        {
            "PNG " => ParsePng(resource.Number, resource.Data),
            "JPEG" => ParseJpeg(resource.Number, resource.Data),
            "Rect" => ParseRectangle(resource.Number, data),
            _ => throw new BlorbFormatException($"Picture resource {resource.Number} uses unsupported chunk type '{resource.Chunk.Type}'.")
        };
    }

    private static BlorbPictureResource ParsePng(uint number, ReadOnlyMemory<byte> encoded)
    {
        ReadOnlySpan<byte> data = encoded.Span;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 33 || !data[..8].SequenceEqual(signature))
        {
            throw new BlorbFormatException($"PNG picture {number} has a truncated header or invalid signature.");
        }

        uint width = 0;
        uint height = 0;
        byte colorType = 0;
        byte bitDepth = 0;
        var palette = ImmutableArray<BlorbColor>.Empty;
        var hasPalette = false;
        var hasTransparency = false;
        var hasImageData = false;
        var adaptiveTransparencyValid = true;
        var cursor = 8;
        var foundHeader = false;
        var foundEnd = false;
        while (cursor < data.Length)
        {
            if (data.Length - cursor < 12)
            {
                throw new BlorbFormatException($"PNG picture {number} has a truncated chunk header.");
            }

            var length = ReadUInt32(data, cursor);
            var type = data.Slice(cursor + 4, 4);
            var chunkData = cursor + 8;
            var chunkEnd = (ulong)chunkData + length + 4;
            if (chunkEnd > (ulong)data.Length)
            {
                throw new BlorbFormatException($"PNG picture {number} has a chunk extending beyond its data.");
            }

            var crcOffset = checked(chunkData + (int)length);
            var storedCrc = ReadUInt32(data, crcOffset);
            var calculatedCrc = CalculatePngCrc(data.Slice(cursor + 4, checked(4 + (int)length)));
            if (storedCrc != calculatedCrc)
            {
                throw new BlorbFormatException($"PNG picture {number} has an invalid chunk checksum.");
            }

            if (!foundHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13)
                {
                    throw new BlorbFormatException($"PNG picture {number} must begin with a 13-byte IHDR chunk.");
                }

                width = ReadUInt32(data, chunkData);
                height = ReadUInt32(data, chunkData + 4);
                colorType = data[chunkData + 9];
                bitDepth = data[chunkData + 8];
                if (width == 0 || height == 0 || !IsValidPngBitDepth(colorType, bitDepth)
                    || data[chunkData + 10] != 0 || data[chunkData + 11] != 0 || data[chunkData + 12] > 1)
                {
                    throw new BlorbFormatException($"PNG picture {number} has invalid dimensions, bit depth, or color type.");
                }

                foundHeader = true;
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                throw new BlorbFormatException($"PNG picture {number} contains more than one IHDR chunk.");
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (hasPalette || hasImageData || length == 0 || length % 3 != 0 || length / 3 > MaximumPngPaletteEntries
                    || colorType is 0 or 4 || colorType == 3 && length / 3 > (1u << bitDepth))
                {
                    throw new BlorbFormatException($"PNG picture {number} has an invalid PLTE chunk.");
                }

                hasPalette = true;
                var paletteBuilder = ImmutableArray.CreateBuilder<BlorbColor>(checked((int)(length / 3)));
                for (var offset = 0u; offset < length; offset += 3)
                {
                    paletteBuilder.Add(new BlorbColor(data[chunkData + checked((int)offset)], data[chunkData + checked((int)offset + 1)], data[chunkData + checked((int)offset + 2)]));
                }

                palette = paletteBuilder.ToImmutable();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                var transparency = data.Slice(chunkData, checked((int)length));
                if (hasImageData || hasTransparency)
                {
                    throw new BlorbFormatException($"PNG picture {number} has a duplicate or misplaced tRNS chunk.");
                }

                var validTransparency = colorType switch
                {
                    0 => transparency.Length == 2,
                    2 => transparency.Length == 6,
                    3 => hasPalette && transparency.Length > 0 && transparency.Length <= palette.Length,
                    _ => false
                };
                if (!validTransparency)
                {
                    throw new BlorbFormatException($"PNG picture {number} has a tRNS chunk invalid for its color type.");
                }

                adaptiveTransparencyValid = colorType == 3 && hasPalette
                    && transparency.Length == 1 && transparency[0] == 0;
                hasTransparency = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                hasImageData = true;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0)
                {
                    throw new BlorbFormatException($"PNG picture {number} has a non-empty IEND chunk.");
                }

                foundEnd = true;
                if (chunkEnd != (ulong)data.Length)
                {
                    throw new BlorbFormatException($"PNG picture {number} has data after its IEND chunk.");
                }

                break;
            }

            cursor = checked((int)chunkEnd);
        }

        if (!foundHeader || !foundEnd || !hasImageData || colorType == 3 && palette.IsDefaultOrEmpty)
        {
            throw new BlorbFormatException($"PNG picture {number} is missing required image or palette data.");
        }

        return new BlorbPictureResource(
            number,
            BlorbPictureFormat.Png,
            width,
            height,
            encoded,
            colorType,
            bitDepth,
            adaptiveTransparencyValid,
            palette);
    }

    private static BlorbPictureResource ParseJpeg(uint number, ReadOnlyMemory<byte> encoded)
    {
        ReadOnlySpan<byte> data = encoded.Span;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8
            || data[^2] != 0xFF || data[^1] != 0xD9)
        {
            throw new BlorbFormatException($"JPEG picture {number} has invalid start or end markers.");
        }

        var cursor = 2;
        while (cursor < data.Length)
        {
            while (cursor < data.Length && data[cursor] != 0xFF)
            {
                cursor++;
            }

            while (cursor < data.Length && data[cursor] == 0xFF)
            {
                cursor++;
            }

            if (cursor >= data.Length)
            {
                break;
            }

            var marker = data[cursor++];
            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7 || marker == 0x01)
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
                break;
            }

            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (segmentLength < 8 || data[cursor + 2] == 0
                    || segmentLength < 8 + (3 * data[cursor + 7]))
                {
                    break;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(cursor + 5, 2));
                if (width == 0 || height == 0)
                {
                    break;
                }

                return new BlorbPictureResource(
                    number,
                    BlorbPictureFormat.Jpeg,
                    width,
                    height,
                    encoded,
                    0,
                    0,
                    false,
                    ImmutableArray<BlorbColor>.Empty);
            }

            cursor += segmentLength;
        }

        throw new BlorbFormatException($"JPEG picture {number} has no valid start-of-frame dimensions.");
    }

    private static BlorbPictureResource ParseRectangle(uint number, ReadOnlySpan<byte> data)
    {
        if (data.Length != 8)
        {
            throw new BlorbFormatException($"Rect picture {number} must contain exactly eight bytes.");
        }

        return new BlorbPictureResource(
            number,
            BlorbPictureFormat.Rectangle,
            ReadUInt32(data, 0),
            ReadUInt32(data, 4),
            ReadOnlyMemory<byte>.Empty,
            0,
            0,
            false,
            ImmutableArray<BlorbColor>.Empty);
    }

    private static bool IsValidPngBitDepth(byte colorType, byte bitDepth) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 or 4 or 6 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        _ => false
    };

    private static uint CalculatePngCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
            }
        }

        return ~crc;
    }

    private static void ValidateRatio(uint numerator, uint denominator, bool allowNoLimit, string description)
    {
        if (numerator == 0 || denominator == 0)
        {
            if (allowNoLimit && numerator == 0 && denominator == 0)
            {
                return;
            }

            throw new BlorbFormatException($"Reso {description} scale ratio must have nonzero numerator and denominator.");
        }
    }

    private static BlorbChunk? GetOptionalSingleton(ImmutableArray<BlorbChunk> chunks, string type)
    {
        var matches = chunks.Where(chunk => chunk.Type == type).ToArray();
        if (matches.Length > 1)
        {
            throw new BlorbFormatException($"Blorb contains more than one '{type}' chunk.");
        }

        return matches.FirstOrDefault();
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, sizeof(uint)));
}
