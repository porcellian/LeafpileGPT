using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Loading;

public sealed class BlorbFileTests
{
    [Fact]
    public void IndexesTypedResourcesAndKeepsOddChunksAndUnknownChunksBounded()
    {
        var blorb = BlorbFixture.Build(
            [new("Pict", 7, "PNG ", Png(4, 5)), new("Snd ", 3, "OGGV", [4, 5, 6, 7, 8])],
            [("XTRA", new byte[] { 9 })]);

        var parsed = BlorbFile.Parse(blorb);

        var picture = parsed.GetResource("Pict", 7);
        Assert.NotNull(picture);
        Assert.Equal("PNG ", picture.Chunk.Id);
        Assert.Equal(Png(4, 5), picture.Chunk.Data.ToArray());
        Assert.Collection(parsed.Resources, _ => { }, _ => { });
        Assert.Equal(new byte[] { 9 }, Assert.Single(parsed.Chunks, chunk => chunk.Id == "XTRA").Data.ToArray());
        Assert.Equal(1, parsed.Chunks.Single(chunk => chunk.Id == "PNG ").Data.Length % 2);
    }

    [Fact]
    public void ParsesResourceOnlyBlorbWithoutRequiringAnEmbeddedExecutable()
    {
        var parsed = BlorbFile.Parse(BlorbFixture.Build([new("Data", 14, "TEXT", "sample"u8.ToArray())]));

        Assert.Empty(parsed.Executables);
        var textResource = parsed.GetResource("Data", 14);
        Assert.NotNull(textResource);
        Assert.Equal("sample", Encoding.ASCII.GetString(textResource.Chunk.Data.Span));
        Assert.Throws<StoryLoadException>(() => parsed.SelectZCodeExecutable());
    }

    [Fact]
    public void SelectsTheFirstCompatibleZCodeEntryWhenSeveralExecutablesAreIndexed()
    {
        var story = MakeStory();
        var parsed = BlorbFile.Parse(BlorbFixture.Build(
        [
            new("Exec", 0, "GLUL", [0, 1, 2, 3]),
            new("Exec", 0, "ZCOD", story)
        ]));

        var selected = parsed.SelectZCodeExecutable();

        Assert.Equal("ZCOD", selected.Chunk.Id);
        Assert.Equal(story, selected.Chunk.Data.ToArray());
    }

    [Fact]
    public void LoadsZCodeFromMultipleExecutableBlorbAndRetainsAllResources()
    {
        var story = MakeStory();
        var blorb = BlorbFixture.Build(
        [
            new("Exec", 0, "GLUL", [0, 1, 2]),
            new("Pict", 4, "Rect", new byte[8]),
            new("Exec", 0, "ZCOD", story)
        ]);

        var loaded = StoryLoader.Load(blorb);

        Assert.Equal(story, loaded.Data.ToArray());
        var blorbFile = loaded.Blorb;
        Assert.NotNull(blorbFile);
        Assert.Collection(blorbFile.Resources, _ => { }, _ => { }, _ => { });
        var picture = blorbFile.GetResource("Pict", 4);
        Assert.NotNull(picture);
        Assert.Equal(new byte[8], picture.Chunk.Data.ToArray());
        Assert.Same(blorbFile, new ZMachineState(loaded).Blorb);
    }

    [Fact]
    public void ParsesOptionalMetadataAndHintChunks()
    {
        var descriptions = new List<byte>();
        WriteUInt32(descriptions, 2);
        WriteDescription(descriptions, "Pict", 1, "cover");
        WriteDescription(descriptions, "Snd ", 3, "bell");
        var resolution = new List<byte>();
        foreach (var value in new uint[] { 640, 400, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0 })
        {
            WriteUInt32(resolution, value);
        }

        var blorb = BlorbFixture.Build(
            [new("Pict", 1, "PNG ", Png(1, 1, [new BlorbColor(1, 2, 3)])), new("Snd ", 3, "OGGV", [2])],
            [
                ("RelN", new byte[] { 0x12, 0x34 }),
                ("AUTH", Encoding.ASCII.GetBytes("Author")),
                ("(c) ", Encoding.ASCII.GetBytes("2026 Owner")),
                ("ANNO", Encoding.ASCII.GetBytes("Notes")),
                ("SNam", Encoding.BigEndianUnicode.GetBytes("Old title")),
                ("IFmd", Encoding.UTF8.GetBytes("<ifindex/>")),
                ("Plte", new byte[] { 1, 2, 3, 4, 5, 6 }),
                ("Fspc", UInt32Bytes(1)),
                ("RDes", descriptions.ToArray()),
                ("Reso", resolution.ToArray()),
                ("APal", UInt32Bytes(1)),
                ("Loop", UInt32Bytes(3, 0))
            ]);

        var parsed = BlorbFile.Parse(blorb);
        var metadata = parsed.Metadata;

        Assert.Equal((ushort)0x1234, metadata.ReleaseNumber);
        Assert.Equal("Author", metadata.Author);
        Assert.Equal("2026 Owner", metadata.Copyright);
        Assert.Equal("Notes", metadata.Annotation);
        Assert.Equal("Old title", metadata.LegacyStoryName);
        Assert.True(metadata.Xml.HasValue);
        Assert.Equal("<ifindex/>", Encoding.UTF8.GetString(metadata.Xml.Value.Span));
        Assert.Equal((uint)1, metadata.FrontispieceNumber);
        Assert.Equal((uint)1, Assert.IsType<BlorbPicture>(parsed.GetFrontispiece()).Number);
        Assert.Equal(new BlorbColor(1, 2, 3), metadata.Palette[0]);
        Assert.Equal(new BlorbColor(4, 5, 6), metadata.Palette[1]);
        Assert.Equal("cover", metadata.ResourceDescriptions[0].Text);
        Assert.Equal((uint)640, metadata.StandardWindowWidth);
        Assert.Equal((uint)400, metadata.StandardWindowHeight);
        Assert.Contains((uint)1, metadata.AdaptivePalettePictures);
        Assert.Equal((uint)0, metadata.Version3SoundRepeats[3]);
    }

    [Fact]
    public void ReadsPngJpegAndRectanglePictureResourcesAndMetadata()
    {
        var palette = new[] { new BlorbColor(12, 34, 56), new BlorbColor(78, 90, 123) };
        var blorb = BlorbFixture.Build(
        [
            new("Pict", 2, "PNG ", Png(40, 20, palette)),
            new("Pict", 9, "JPEG", Jpeg(320, 200)),
            new("Pict", 15, "Rect", UInt32Bytes(12, 7))
        ],
        [
            ("RDes", ResourceDescription("Pict", 9, "a photograph"))
        ]);
        var parsed = BlorbFile.Parse(blorb);

        var png = Assert.IsType<BlorbPicture>(parsed.GetPicture(2));
        Assert.Equal(BlorbPictureFormat.Png, png.Format);
        Assert.Equal(new BlorbPictureSize(40, 20), new BlorbPictureSize(png.Width, png.Height));
        Assert.Equal(palette, png.Palette);
        Assert.False(png.UsesAdaptivePalette);
        Assert.Equal(Png(40, 20, palette), png.Data.ToArray());

        var jpeg = Assert.IsType<BlorbPicture>(parsed.GetPicture(9));
        Assert.Equal(BlorbPictureFormat.Jpeg, jpeg.Format);
        Assert.Equal(320u, jpeg.Width);
        Assert.Equal(200u, jpeg.Height);
        Assert.Equal("a photograph", jpeg.Description);
        Assert.False(jpeg.UsesAdaptivePalette);

        var rectangle = Assert.IsType<BlorbPicture>(parsed.GetPicture(15));
        Assert.Equal(BlorbPictureFormat.Rectangle, rectangle.Format);
        Assert.Equal(12u, rectangle.Width);
        Assert.Equal(7u, rectangle.Height);
        Assert.Null(parsed.GetPicture(3));
        Assert.Equal(3, parsed.PictureCount);

        var adaptive = BlorbFile.Parse(BlorbFixture.Build(
            [new("Pict", 9, "PNG ", Png(30, 15, [new BlorbColor(1, 2, 3)]))],
            [("APal", UInt32Bytes(9))]));
        Assert.True(Assert.IsType<BlorbPicture>(adaptive.GetPicture(9)).UsesAdaptivePalette);
    }

    [Fact]
    public void ComputesResolutionScalingWithAspectRatioAndBounds()
    {
        var resolution = UInt32Bytes(640, 400, 0, 0, 0, 0, 4, 2, 1, 1, 2, 3, 1);
        var parsed = BlorbFile.Parse(BlorbFixture.Build(
            [new("Pict", 4, "PNG ", Png(100, 50))],
            [("Reso", resolution)]));
        var picture = Assert.IsType<BlorbPicture>(parsed.GetPicture(4));

        Assert.Equal(new BlorbPictureSize(200, 100), picture.GetDisplaySize(parsed.Metadata, 640, 400));
        Assert.Equal(new BlorbPictureSize(100, 50), picture.GetDisplaySize(parsed.Metadata, 320, 200));
        Assert.Equal(new BlorbPictureSize(300, 150), picture.GetDisplaySize(parsed.Metadata, 1280, 800));
    }

    [Theory]
    [InlineData("PNG ", new byte[] { 1, 2, 3 })]
    [InlineData("JPEG", new byte[] { 0xff, 0xd8, 0xff, 0xd9 })]
    public void RejectsMalformedImageHeadersWhenPictureIsQueried(string id, byte[] data)
    {
        var parsed = BlorbFile.Parse(BlorbFixture.Build([new("Pict", 1, id, data)]));

        Assert.Throws<StoryLoadException>(() => parsed.GetPicture(1));
    }

    [Fact]
    public void RejectsPictureResourceNumberZero()
    {
        var blorb = BlorbFixture.Build([new("Pict", 0, "Rect", new byte[8])]);

        Assert.Contains("number 0", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(blorb)).Message);
    }

    [Fact]
    public void ValidatesEmbeddedZCodeIdentifierAndInitialProgramCounter()
    {
        var story = MakeStory();
        var identifier = new byte[13];
        story.AsSpan(2, 2).CopyTo(identifier);
        story.AsSpan(0x12, 6).CopyTo(identifier.AsSpan(2));
        story.AsSpan(0x1c, 2).CopyTo(identifier.AsSpan(8));
        var valid = BlorbFixture.Build([new("Exec", 0, "ZCOD", story)], [("IFhd", identifier)]);

        Assert.Equal(story, StoryLoader.Load(valid).Data.ToArray());

        identifier[10] = 1;
        var invalidPc = BlorbFixture.Build([new("Exec", 0, "ZCOD", story)], [("IFhd", identifier)]);
        Assert.Contains("initial-PC", Assert.Throws<StoryLoadException>(() => StoryLoader.Load(invalidPc)).Message);
        identifier[10] = 0;
        identifier[9] ^= 1;
        var mismatch = BlorbFixture.Build([new("Exec", 0, "ZCOD", story)], [("IFhd", identifier)]);
        Assert.Contains("does not match", Assert.Throws<StoryLoadException>(() => StoryLoader.Load(mismatch)).Message);
    }

    [Fact]
    public void RejectsRIdxThatIsNotFirstOrHasIncorrectEntryLength()
    {
        var valid = BlorbFixture.Build([new("Data", 1, "BINA", [0])]);
        var reordered = BlorbFixture.InsertChunkBeforeRidx(valid, "JUNK", [0]);
        var wrongLength = (byte[])valid.Clone();
        WriteUInt32(wrongLength, 20, 2);
        WriteUInt32(wrongLength, 4, (uint)(wrongLength.Length - 8));

        Assert.Contains("first chunk", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(reordered)).Message);
        Assert.Contains("require", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(wrongLength)).Message);
    }

    [Fact]
    public void RejectsMissingResourceOffsetsDuplicateKeysAndOutOfOrderIndexes()
    {
        var missing = BlorbFixture.Build([new("Data", 1, "BINA", [0])]);
        WriteUInt32(missing, 32, 0x7fffffff);
        var duplicate = BlorbFixture.Build([new("Data", 1, "BINA", [0]), new("Data", 1, "BINA", [1])]);
        var outOfOrder = BlorbFixture.Build([new("Data", 1, "BINA", [0]), new("Data", 2, "BINA", [1])]);
        WriteUInt32(outOfOrder, 32, 58);
        WriteUInt32(outOfOrder, 44, 48);

        Assert.Contains("refers to no chunk", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(missing)).Message);
        Assert.Contains("duplicate resource", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(duplicate)).Message);
        Assert.Contains("same order", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(outOfOrder)).Message);
    }

    [Fact]
    public void RejectsMalformedIFFPaddingAndInvalidResourceTypePairs()
    {
        var nonzeroPad = BlorbFixture.Build([new("Data", 1, "BINA", [0x44])]);
        nonzeroPad[^1] = 0x99;
        var incompatible = BlorbFixture.Build([new("Pict", 1, "TEXT", [1])]);

        Assert.Contains("nonzero IFF pad", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(nonzeroPad)).Message);
        Assert.Contains("incompatible chunk type", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(incompatible)).Message);
    }

    [Theory]
    [InlineData("RelN", new byte[] { 1 })]
    [InlineData("Plte", new byte[] { 1, 2 })]
    [InlineData("Fspc", new byte[] { 1, 2 })]
    [InlineData("APal", new byte[] { 1, 2 })]
    [InlineData("Loop", new byte[] { 1, 2 })]
    public void RejectsMalformedKnownHintChunkLengths(string id, byte[] data)
    {
        var blorb = BlorbFixture.Build([], [(id, data)]);

        Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(blorb));
    }

    [Fact]
    public void RejectsInvalidDescriptionCountsUTF8AndDuplicateSingleChunks()
    {
        var badDescriptionCount = BlorbFixture.Build([], [("RDes", UInt32Bytes(1))]);
        var badUtf8Description = new List<byte>();
        WriteUInt32(badUtf8Description, 1);
        WriteFourCc(badUtf8Description, "Pict");
        WriteUInt32(badUtf8Description, 1);
        WriteUInt32(badUtf8Description, 1);
        badUtf8Description.Add(0xff);
        var badUtf8 = BlorbFixture.Build([], [("RDes", badUtf8Description.ToArray())]);
        var duplicateRelN = BlorbFixture.Build([], [("RelN", [0, 1]), ("RelN", [0, 2])]);

        Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(badDescriptionCount));
        Assert.Contains("UTF-8", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(badUtf8)).Message);
        Assert.Contains("more than one", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(duplicateRelN)).Message);
    }

    [Fact]
    public void ParsesDirectColorAndRejectsInvalidResolutionDimensionsAndFrontispiece()
    {
        var directColor = BlorbFile.Parse(BlorbFixture.Build([new("Pict", 1, "Rect", new byte[8])], [("Plte", [16])])).Metadata;
        Assert.Equal((byte)16, directColor.PreferredColorDepth);

        var badResolution = new byte[24];
        var invertedRatios = UInt32Bytes(640, 400, 0, 0, 0, 0, 1, 1, 1, 2, 1, 1, 1);
        var resolutionBlorb = BlorbFixture.Build([], [("Reso", badResolution)]);
        var ratioBlorb = BlorbFixture.Build([], [("Reso", invertedRatios)]);
        var missingFrontispiece = BlorbFixture.Build([], [("Fspc", UInt32Bytes(9))]);
        var rectangleFrontispiece = BlorbFixture.Build([new("Pict", 9, "Rect", new byte[8])], [("Fspc", UInt32Bytes(9))]);

        Assert.Contains("window dimensions", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(resolutionBlorb)).Message);
        Assert.Contains("minimum image ratio", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(ratioBlorb)).Message);
        Assert.Contains("missing picture", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(missingFrontispiece)).Message);
        Assert.Contains("placeholder Rect", Assert.Throws<StoryLoadException>(() => BlorbFile.Parse(rectangleFrontispiece)).Message);
    }

    private static byte[] MakeStory()
    {
        var story = new byte[1024];
        story[0] = 5;
        WriteWord(story, 0x04, 0x0300);
        WriteWord(story, 0x06, 0x0200);
        WriteWord(story, 0x08, 0x0300);
        WriteWord(story, 0x0a, 0x0040);
        WriteWord(story, 0x0c, 0x0040);
        WriteWord(story, 0x0e, 0x0300);
        WriteWord(story, 0x18, 0x0100);
        WriteWord(story, 0x1a, 0x0100);
        WriteWord(story, 0x1c, 0x1234);
        story[0x12] = (byte)'A';
        story[0x13] = (byte)'B';
        story[0x14] = (byte)'C';
        story[0x15] = (byte)'D';
        story[0x16] = (byte)'E';
        story[0x17] = (byte)'F';
        return story;
    }

    private static void WriteDescription(List<byte> data, string usage, uint number, string text)
    {
        WriteFourCc(data, usage);
        WriteUInt32(data, number);
        var encoded = Encoding.UTF8.GetBytes(text);
        WriteUInt32(data, (uint)encoded.Length);
        data.AddRange(encoded);
    }

    private static byte[] UInt32Bytes(params uint[] values)
    {
        var data = new List<byte>();
        foreach (var value in values)
        {
            WriteUInt32(data, value);
        }

        return data.ToArray();
    }

    private static byte[] ResourceDescription(string usage, uint number, string text)
    {
        var data = new List<byte>();
        WriteUInt32(data, 1);
        WriteDescription(data, usage, number, text);
        return data.ToArray();
    }

    private static byte[] Png(uint width, uint height, IReadOnlyList<BlorbColor>? colors = null)
    {
        var data = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = colors is null ? (byte)2 : (byte)3;
        AddPngChunk(data, "IHDR", header);
        if (colors is not null)
        {
            AddPngChunk(data, "PLTE", colors.SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray());
        }

        AddPngChunk(data, "IDAT", [0, 0]);
        AddPngChunk(data, "IEND", []);
        return data.ToArray();
    }

    private static byte[] Jpeg(ushort width, ushort height) =>
    [
        0xff, 0xd8, // SOI
        0xff, 0xc0, 0x00, 0x0b, 0x08,
        (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
        0x01, 0x01, 0x11, 0x00
    ];

    private static void AddPngChunk(List<byte> target, string id, byte[] data)
    {
        WriteUInt32(target, (uint)data.Length);
        WriteFourCc(target, id);
        target.AddRange(data);
        WriteUInt32(target, 0); // CRC is not needed for image dimensions or palette metadata.
    }

    private static void WriteFourCc(List<byte> data, string value) => data.AddRange(Encoding.ASCII.GetBytes(value));

    private static void WriteUInt32(List<byte> data, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        data.AddRange(bytes.ToArray());
    }

    private static void WriteWord(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    private static class BlorbFixture
    {
        public static byte[] Build(IReadOnlyList<ResourceSpec> resources, IReadOnlyList<(string Id, byte[] Data)>? optional = null)
        {
            optional ??= [];
            var indexDataLength = 4 + (resources.Count * 12);
            var cursor = 12 + 8 + indexDataLength;
            var offsets = new int[resources.Count];
            for (var index = 0; index < resources.Count; index++)
            {
                offsets[index] = cursor;
                cursor += 8 + resources[index].Data.Length + (resources[index].Data.Length & 1);
            }

            foreach (var chunk in optional)
            {
                cursor += 8 + chunk.Data.Length + (chunk.Data.Length & 1);
            }

            var result = new byte[cursor];
            WriteFourCc(result, 0, "FORM");
            WriteUInt32(result, 4, checked((uint)(result.Length - 8)));
            WriteFourCc(result, 8, "IFRS");
            WriteFourCc(result, 12, "RIdx");
            WriteUInt32(result, 16, checked((uint)indexDataLength));
            WriteUInt32(result, 20, checked((uint)resources.Count));
            for (var index = 0; index < resources.Count; index++)
            {
                var entry = 24 + (index * 12);
                WriteFourCc(result, entry, resources[index].Usage);
                WriteUInt32(result, entry + 4, resources[index].Number);
                WriteUInt32(result, entry + 8, checked((uint)offsets[index]));
            }

            cursor = 12 + 8 + indexDataLength;
            foreach (var resource in resources)
            {
                cursor = WriteChunk(result, cursor, resource.ChunkId, resource.Data);
            }

            foreach (var chunk in optional)
            {
                cursor = WriteChunk(result, cursor, chunk.Id, chunk.Data);
            }

            return result;
        }

        public static byte[] InsertChunkBeforeRidx(byte[] source, string id, byte[] data)
        {
            var insertedChunkLength = 8 + data.Length + (data.Length & 1);
            var result = new byte[source.Length + insertedChunkLength];
            source.AsSpan(0, 12).CopyTo(result);
            WriteUInt32(result, 4, checked((uint)(result.Length - 8)));
            WriteFourCc(result, 12, id);
            WriteUInt32(result, 16, checked((uint)data.Length));
            data.CopyTo(result, 20);
            source.AsSpan(12).CopyTo(result.AsSpan(12 + insertedChunkLength));
            return result;
        }

        private static int WriteChunk(byte[] target, int offset, string id, byte[] data)
        {
            WriteFourCc(target, offset, id);
            WriteUInt32(target, offset + 4, checked((uint)data.Length));
            data.CopyTo(target, offset + 8);
            var end = offset + 8 + data.Length;
            if ((data.Length & 1) != 0)
            {
                target[end] = 0;
                end++;
            }

            return end;
        }

        private static void WriteFourCc(byte[] data, int offset, string value) =>
            Encoding.ASCII.GetBytes(value).CopyTo(data, offset);

        private static void WriteUInt32(byte[] data, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    }

    private sealed record ResourceSpec(string Usage, uint Number, string ChunkId, byte[] Data);

    private static ResourceSpec New(string usage, uint number, string chunkId, byte[] data) => new(usage, number, chunkId, data);
}
