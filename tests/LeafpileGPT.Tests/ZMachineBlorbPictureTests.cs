namespace LeafpileGPT.Tests;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineBlorbPictureTests
{
    private const uint CodeAddress = 850;

    [Fact]
    public void ReadsPngJpegAndRectanglePictureDimensions()
    {
        var parsed = BlorbFile.Parse(CreateBlorb(
            [
                new("PNG ", Png(64, 32), "Pict", 1),
                new("JPEG", Jpeg(320, 200), "Pict", 4),
                new("Rect", Dword(800, 600), "Pict", 9)
            ]));

        Assert.True(parsed.Pictures.TryGetPicture(1, 640, 400, out var png));
        Assert.Equal((uint)64, png!.Resource.PixelWidth);
        Assert.Equal((uint)32, png.Resource.PixelHeight);
        Assert.Equal(BlorbPictureFormat.Png, png.Resource.Format);
        Assert.Equal(Png(64, 32), png.Resource.EncodedData.ToArray());
        Assert.True(parsed.Pictures.TryGetPicture(4, 640, 400, out var jpeg));
        Assert.Equal((uint)320, jpeg!.Resource.PixelWidth);
        Assert.Equal((uint)200, jpeg.Resource.PixelHeight);
        Assert.True(parsed.Pictures.TryGetPicture(9, 640, 400, out var rectangle));
        Assert.Equal(BlorbPictureFormat.Rectangle, rectangle!.Resource.Format);
        Assert.Equal((uint)800, rectangle.Resource.PixelWidth);
        Assert.Equal((uint)600, rectangle.Resource.PixelHeight);
        Assert.False(parsed.Pictures.TryGetPicture(2, 640, 400, out _));
    }

    [Fact]
    public void ParsesPaletteHintsAndUpdatesAdaptivePaletteWhenPicturesArePlotted()
    {
        var redPalette = PngPalette(new BlorbColor(10, 20, 30), new BlorbColor(40, 50, 60), new BlorbColor(70, 80, 90));
        var parsed = BlorbFile.Parse(CreateBlorb(
            [
                new("PNG ", Png(4, 3, colorType: 3, redPalette), "Pict", 1),
                new("PNG ", Png(2, 2, colorType: 3,
                    PngPalette(new BlorbColor(1, 2, 3), new BlorbColor(4, 5, 6), new BlorbColor(7, 8, 9))), "Pict", 8)
            ],
            [
                new("Plte", [255, 0, 0, 0, 255, 0]),
                new("APal", UInt32(8)),
                new("RelN", Word(41)),
                new("Fspc", UInt32(1))
            ]));

        Assert.Equal(new BlorbColor(255, 0, 0), parsed.Pictures.PaletteHint[0]);
        Assert.Null(parsed.Pictures.DirectColorDepthHint);
        Assert.True(parsed.Pictures.HasAdaptivePalette);
        Assert.Equal(new uint[] { 8 }, parsed.Pictures.AdaptivePictureNumbers);
        Assert.Equal((uint)1, parsed.Pictures.FrontispieceNumber);
        Assert.Equal((ushort)41, parsed.Pictures.ReleaseNumber);

        Assert.Empty(parsed.Pictures.RecordPicturePlotted(1));
        Assert.Equal(16, parsed.Pictures.CurrentPalette.Length);
        Assert.Equal(new BlorbColor(70, 80, 90), parsed.Pictures.CurrentPalette[2]);
        Assert.True(parsed.Pictures.TryGetPicture(8, 640, 400, out var adaptive));
        Assert.True(adaptive!.UsesAdaptivePalette);
        Assert.Equal(parsed.Pictures.CurrentPalette, adaptive.PaletteOverride);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void ParsesDirectColorDepthHints(byte depth)
    {
        var parsed = BlorbFile.Parse(CreateBlorb([], [new("Plte", [depth])]));
        Assert.Equal(depth, parsed.Pictures.DirectColorDepthHint);
        Assert.Empty(parsed.Pictures.PaletteHint);
    }

    [Fact]
    public void AppliesResolutionScalingAndKeepsUnlistedImagesAtNativeSize()
    {
        var parsed = BlorbFile.Parse(CreateBlorb(
            [new("Rect", Dword(100, 50), "Pict", 2), new("Rect", Dword(5, 7), "Pict", 7)],
            [new("Reso", Resolution(600, 400, new(2, 2, 1, 3, 2, 3, 1)))]));

        Assert.Equal(new BlorbWindowSizeHints(600, 400, 0, 0, 0, 0), parsed.Pictures.WindowSizeHints);
        Assert.True(parsed.Pictures.TryGetPicture(2, 600, 400, out var standard));
        Assert.Equal((200, 100), (standard!.DisplayWidth, standard.DisplayHeight));
        Assert.True(parsed.Pictures.TryGetPicture(2, 300, 200, out var smaller));
        Assert.Equal((150, 75), (smaller!.DisplayWidth, smaller.DisplayHeight));
        Assert.True(parsed.Pictures.TryGetPicture(2, 1200, 800, out var larger));
        Assert.Equal((300, 150), (larger!.DisplayWidth, larger.DisplayHeight));
        Assert.True(parsed.Pictures.TryGetPicture(7, 1200, 800, out var unscaled));
        Assert.Equal((5, 7), (unscaled!.DisplayWidth, unscaled.DisplayHeight));
    }

    [Fact]
    public void RejectsMalformedPictureChunksAndDisplayHints()
    {
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([new("PNG ", [1, 2, 3], "Pict", 1)])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([new("JPEG", [0, 1, 2], "Pict", 1)])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb(
            [new("JPEG", Jpeg(1, 1)[..^2], "Pict", 1)])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([new("Rect", [1, 2], "Pict", 1)])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([], [new("Plte", [8])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([], [new("Reso", new byte[25])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb(
            [new("Rect", Dword(1, 1), "Pict", 1)], [new("Reso", Resolution(10, 10, new(1, 0, 0, 0, 0, 0, 0)))])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb(
            [new("Rect", Dword(1, 1), "Pict", 1)], [new("Reso", Resolution(10, 10, new(1, 1, 1, 3, 1, 2, 1)))])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([], [new("APal", UInt32(1))])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb(
            [new("JPEG", Jpeg(1, 1), "Pict", 1)], [new("APal", [])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb(
            [new("PNG ", Png(1, 1, colorType: 3,
                PngPalette(new BlorbColor(0, 0, 0), new BlorbColor(1, 1, 1), new BlorbColor(2, 2, 2)), [255]), "Pict", 1)],
            [new("APal", [])])));

        var badCrc = Png(1, 1);
        badCrc[29] ^= 0xFF;
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(CreateBlorb([new("PNG ", badCrc, "Pict", 1)])));
    }

    [Fact]
    public void PictureDataWritesDimensionsBranchesAndReportsCatalogSummary()
    {
        var catalog = BlorbFile.Parse(CreateBlorb(
            [new("Rect", Dword(320, 200), "Pict", 6)], [new("RelN", Word(73))])).Pictures;
        var state = CreateV6State(catalog);
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("picture_data", [Constant(6), Constant(500)], branch: Branch(true)), state);
        Assert.Equal((ushort)200, state.Memory.ReadWord(500));
        Assert.Equal((ushort)320, state.Memory.ReadWord(502));
        Assert.Equal(CodeAddress + 3, state.ProgramCounter);

        state.SetProgramCounter(CodeAddress);
        executor.Execute(Instruction("picture_data", [Constant(0), Constant(504)], branch: Branch(true)), state);
        Assert.Equal((ushort)1, state.Memory.ReadWord(504));
        Assert.Equal((ushort)73, state.Memory.ReadWord(506));
        Assert.Equal(CodeAddress + 3, state.ProgramCounter);

        state.SetProgramCounter(CodeAddress);
        state.Memory.WriteWord(508, 0xCAFE);
        executor.Execute(Instruction("picture_data", [Constant(3), Constant(508)], branch: Branch(true)), state);
        Assert.Equal(CodeAddress + 1, state.ProgramCounter);
        Assert.Equal((ushort)0xCAFE, state.Memory.ReadWord(508));
    }

    [Fact]
    public void DrawAndErasePictureEmitWindowOperationsAndPictureTableIsOptional()
    {
        var catalog = BlorbFile.Parse(CreateBlorb([new("Rect", Dword(20, 10), "Pict", 2)])).Pictures;
        var state = CreateV6State(catalog);
        var operations = new List<ZMachineScreenOperation>();
        state.OutputStreams.Screen.OperationWriter = operations.Add;
        state.OutputStreams.Screen.SetV6Cursor(4, 5);
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("draw_picture", [Constant(2), Constant(0), Constant(0)]), state);
        var draw = Assert.Single(operations, item => item.Kind == ZMachineScreenOperationKind.DrawPicture);
        Assert.Equal(4, draw.Row);
        Assert.Equal(5, draw.Column);
        Assert.Equal((uint)2, draw.Picture!.Resource.Number);

        executor.Execute(Instruction("erase_picture", [Constant(2), Constant(9), Constant(11)]), state);
        Assert.Contains(operations, item => item.Kind == ZMachineScreenOperationKind.ErasePicture
            && item.Row == 9 && item.Column == 11);
        executor.Execute(Instruction("picture_table", [Constant(0)]), state);
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction("draw_picture", [Constant(3), Constant(1), Constant(1)]), state));
    }

    [Fact]
    public void SetsV6PictureCapabilityOnlyWhenPicturesAreAvailableAndPreservesGameRequest()
    {
        var storyBytes = StoryFixtureBuilder.Create(6);
        storyBytes[800] = 0;
        StoryFixtureBuilder.WriteWord(storyBytes, 0x10, 0x0008);
        StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        var story = StoryFile.Parse(storyBytes);

        var withoutPictures = ZMachineExecutionState.Start(story);
        Assert.Equal((byte)0, (byte)(withoutPictures.Memory.ReadByte(1) & 0x02));
        Assert.Equal((ushort)0x0008, (ushort)(withoutPictures.Memory.ReadWord(0x10) & 0x0008));

        var withPictures = ZMachineExecutionState.Start(
            story,
            pictureCatalog: BlorbFile.Parse(CreateBlorb([new("Rect", Dword(2, 3), "Pict", 1)])).Pictures);
        Assert.Equal((byte)0x02, (byte)(withPictures.Memory.ReadByte(1) & 0x02));
        Assert.Equal((ushort)0x0008, (ushort)(withPictures.Memory.ReadWord(0x10) & 0x0008));
    }

    [Fact]
    public void VersionFiveAdvertisesCharacterGraphicsButDoesNotSetPictureRequestInVersionSix()
    {
        var v5 = ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(5)));
        Assert.Equal((ushort)0x0008, (ushort)(v5.Memory.ReadWord(0x10) & 0x0008));
        var v6 = CreateV6State(pictureCatalog: null);
        Assert.Equal((ushort)0, (ushort)(v6.Memory.ReadWord(0x10) & 0x0008));
    }

    private static ZMachineExecutionState CreateV6State(BlorbPictureCatalog? pictureCatalog)
    {
        var bytes = StoryFixtureBuilder.Create(6);
        bytes[800] = 0;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes), pictureCatalog: pictureCatalog);
    }

    private static DecodedBranch Branch(bool branchOnTrue) => new(branchOnTrue, 2, CodeAddress + 3, null);

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);

    private static DecodedInstruction Instruction(string mnemonic, DecodedOperand[] operands, DecodedBranch? branch = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Extended, 0, mnemonic, 6, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), false, branch is not null);
        return new DecodedInstruction(CodeAddress, CodeAddress + 1, InstructionForm.Extended,
            OpcodeClass.Extended, 0, definition, operands.ToImmutableArray(), null, branch, ImmutableArray<byte>.Empty);
    }

    private static byte[] Png(
        uint width,
        uint height,
        byte colorType = 2,
        byte[]? palette = null,
        byte[]? transparency = null)
    {
        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = colorType;
        WritePngChunk(stream, "IHDR", header);
        if (palette is not null)
        {
            WritePngChunk(stream, "PLTE", palette);
        }

        if (transparency is not null)
        {
            WritePngChunk(stream, "tRNS", transparency);
        }

        WritePngChunk(stream, "IDAT", CompressPngRows(width, height, colorType));
        WritePngChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static byte[] CompressPngRows(uint width, uint height, byte colorType)
    {
        var channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType))
        };
        var rowLength = checked((int)(width * (uint)channels));
        var filtered = new byte[checked((rowLength + 1) * (int)height)];
        if (colorType == 3)
        {
            for (var row = 0u; row < height; row++)
            {
                Array.Fill(filtered, (byte)2, checked((int)row * (rowLength + 1) + 1), rowLength);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        return compressed.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)data.Length));
        stream.Write(size);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var checksumInput = typeBytes.Concat(data).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(size, PngCrc32(checksumInput));
        stream.Write(size);
    }

    private static uint PngCrc32(ReadOnlySpan<byte> data)
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

    private static byte[] PngPalette(params BlorbColor[] colors)
    {
        var bytes = new byte[colors.Length * 3];
        for (var index = 0; index < colors.Length; index++)
        {
            bytes[index * 3] = colors[index].Red;
            bytes[(index * 3) + 1] = colors[index].Green;
            bytes[(index * 3) + 2] = colors[index].Blue;
        }

        return bytes;
    }

    private static byte[] Jpeg(ushort width, ushort height) =>
    [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8,
        (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        1, 1, 0x11, 0, 0xFF, 0xD9];

    private static byte[] Dword(uint first, uint second)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, first);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), second);
        return bytes;
    }

    private static byte[] Word(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Resolution(uint standardWidth, uint standardHeight, BlorbImageScale scale)
    {
        var data = new byte[52];
        BinaryPrimitives.WriteUInt32BigEndian(data, standardWidth);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), standardHeight);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(24), scale.PictureNumber);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(28), scale.StandardNumerator);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), scale.StandardDenominator);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(36), scale.MinimumNumerator);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), scale.MinimumDenominator);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), scale.MaximumNumerator);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(48), scale.MaximumDenominator);
        return data;
    }

    private sealed record FixtureChunk(string Type, byte[] Data, string? Usage = null, uint Number = 0);

    private static byte[] CreateBlorb(FixtureChunk[] resources, FixtureChunk[]? optional = null)
    {
        optional ??= [];
        var index = new byte[4 + resources.Length * 12];
        BinaryPrimitives.WriteUInt32BigEndian(index, checked((uint)resources.Length));
        var chunks = new[] { new FixtureChunk("RIdx", index) }.Concat(optional).Concat(resources).ToArray();
        var offset = 12;
        foreach (var chunk in chunks)
        {
            if (chunk.Usage is not null)
            {
                var resourceIndex = Array.IndexOf(resources, chunk);
                var entry = 4 + resourceIndex * 12;
                Encoding.ASCII.GetBytes(chunk.Usage).CopyTo(index, entry);
                BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(entry + 4), chunk.Number);
                BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(entry + 8), checked((uint)offset));
            }

            offset += 8 + chunk.Data.Length + (chunk.Data.Length & 1);
        }

        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("IFRS"));
        Span<byte> length = stackalloc byte[4];
        foreach (var chunk in chunks)
        {
            body.Write(Encoding.ASCII.GetBytes(chunk.Type));
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)chunk.Data.Length));
            body.Write(length);
            body.Write(chunk.Data);
            if ((chunk.Data.Length & 1) != 0)
            {
                body.WriteByte(0);
            }
        }

        var payload = body.ToArray();
        var file = new byte[8 + payload.Length];
        Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), checked((uint)payload.Length));
        payload.CopyTo(file, 8);
        return file;
    }
}
