namespace LeafpileGPT.Tests;

using System.Buffers.Binary;
using System.Text;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineBlorbTests
{
    [Fact]
    public void ParsesResourceIndexEmbeddedZCodeAndOptionalMetadata()
    {
        var story = StoryFixtureBuilder.Create(5);
        var file = BlorbFixtureBuilder.Create(
            [
                new("ZCOD", story, "Exec", 0),
                new("Rect", [0, 0, 0, 4, 0, 0, 0, 4], "Pict", 7),
                new("CUST", [0xCA, 0xFE], "Data", 3)
            ],
            [
                new("RelN", Word(123)),
                new("IFhd", [1, 2, 3, 4]),
                new("IFmd", Encoding.UTF8.GetBytes("<ifindex/>")),
                new("RDes", ResourceDescription("Pict", 7, "A café"))
            ]);

        var parsed = BlorbFile.Parse(file);

        Assert.Equal((ushort)123, parsed.ReleaseNumber);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, parsed.GameIdentifier);
        Assert.Equal("<ifindex/>", Encoding.UTF8.GetString(parsed.Metadata.AsSpan()));
        Assert.Equal(3, parsed.Resources.Length);
        Assert.Equal("CUST", parsed.FindChunk("CUST")!.Type);
        Assert.Equal(new byte[] { 0, 0, 0, 4, 0, 0, 0, 4 }, parsed.FindResource("Pict", 7)!.Data.ToArray());
        Assert.Equal("A café", Assert.Single(parsed.ResourceDescriptions).Text);
        Assert.Equal("ZCOD", parsed.SelectedExecutable!.Chunk.Type);
        Assert.Equal((byte)5, parsed.ZCodeStory!.Header.Version);
    }

    [Fact]
    public void ParsesAnExternalResourceFormWithoutAnExecutable()
    {
        var parsed = BlorbFile.Parse(BlorbFixtureBuilder.Create(
            [new("OGGV", [1, 2, 3], "Snd ", 4)]));

        Assert.Null(parsed.SelectedExecutable);
        Assert.Null(parsed.ZCodeStory);
        Assert.Equal((uint)4, parsed.FindResource("Snd ", 4)!.Number);
    }

    [Fact]
    public void KeepsUnknownChunksAndAcceptsZeroPaddedOddLengthData()
    {
        var parsed = BlorbFile.Parse(BlorbFixtureBuilder.Create(
            [],
            [new("XTRA", [1, 2, 3])]));

        var chunk = Assert.Single(parsed.Chunks.Where(item => item.Type == "XTRA"));
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk.Data.ToArray());
    }

    [Fact]
    public void RejectsInvalidFormIdentifierOrType()
    {
        var file = BlorbFixtureBuilder.Create([]);
        Encoding.ASCII.GetBytes("NOPE").CopyTo(file, 0);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file));

        file = BlorbFixtureBuilder.Create([]);
        Encoding.ASCII.GetBytes("NOPE").CopyTo(file, 8);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file));
    }

    [Fact]
    public void RejectsTruncatedFormAndDeclaredLengthMismatch()
    {
        var file = BlorbFixtureBuilder.Create([]);

        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file.AsSpan(0, file.Length - 1)));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), checked((uint)(file.Length - 7)));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file));
    }

    [Fact]
    public void RequiresExactlyOneResourceIndexAsTheFirstChunk()
    {
        var file = BlorbFixtureBuilder.Create([], [new("ANNO", [0]), new("RIdx", [0, 0, 0, 0])], indexFirst: false);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file));

        var missing = BlorbFixtureBuilder.Create([]);
        Encoding.ASCII.GetBytes("ANNO").CopyTo(missing, 12);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(missing));

        var duplicated = BlorbFixtureBuilder.Create([], [new("RIdx", [0, 0, 0, 0])], allowDuplicateIndex: true);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(duplicated));
    }

    [Fact]
    public void RejectsTruncatedChunkHeaderAndOutOfBoundsChunkData()
    {
        var truncatedHeader = BlorbFixtureBuilder.Create([]);
        Array.Resize(ref truncatedHeader, truncatedHeader.Length + 1);
        BinaryPrimitives.WriteUInt32BigEndian(truncatedHeader.AsSpan(4), checked((uint)(truncatedHeader.Length - 8)));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(truncatedHeader));

        var truncatedData = BlorbFixtureBuilder.Create([], [new("DATA", [1, 2])]);
        var dataChunkHeader = 12 + 12;
        BinaryPrimitives.WriteUInt32BigEndian(truncatedData.AsSpan(dataChunkHeader + 4), 500);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(truncatedData));
    }

    [Fact]
    public void RequiresZeroPaddingForOddLengthChunks()
    {
        var file = BlorbFixtureBuilder.Create([], [new("ODD!", [1, 2, 3])]);
        var padOffset = 12 + 12 + 8 + 3;
        file[padOffset] = 1;

        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(file));

        var missingPadding = BlorbFixtureBuilder.Create([], [new("ODD!", [1, 2, 3])]);
        Array.Resize(ref missingPadding, missingPadding.Length - 1);
        BinaryPrimitives.WriteUInt32BigEndian(missingPadding.AsSpan(4), checked((uint)(missingPadding.Length - 8)));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(missingPadding));
    }

    [Fact]
    public void RejectsResourceIndexLengthCountAndOffsetsThatDoNotPointAtChunks()
    {
        var wrongCount = BlorbFixtureBuilder.Create([new("TEXT", [5], "Data", 1)]);
        BinaryPrimitives.WriteUInt32BigEndian(wrongCount.AsSpan(20), 2);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(wrongCount));

        var wrongOffset = BlorbFixtureBuilder.Create([new("TEXT", [5], "Data", 1)]);
        BinaryPrimitives.WriteUInt32BigEndian(wrongOffset.AsSpan(32), 13);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(wrongOffset));
    }

    [Fact]
    public void RejectsNonzeroExecutableNumberAndSelectsZCodeOnlyFromZcod()
    {
        var nonzeroExecutable = BlorbFixtureBuilder.Create([new("ZCOD", [0], "Exec", 1)]);
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(nonzeroExecutable));

        var otherVm = BlorbFile.Parse(BlorbFixtureBuilder.Create([new("GLUL", [1], "Exec", 0)]));
        Assert.Equal("GLUL", otherVm.SelectedExecutable!.Chunk.Type);
        Assert.Null(otherVm.ZCodeStory);

        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([new("ZCOD", [0], "Exec", 0)])));
    }

    [Fact]
    public void SelectsTheFirstIndexedExecutableAndRejectsDuplicateResourceNumbers()
    {
        var firstStory = StoryFixtureBuilder.Create(5);
        var secondStory = StoryFixtureBuilder.Create(3);
        var multipleExecutables = BlorbFile.Parse(BlorbFixtureBuilder.Create(
        [
            new("ZCOD", firstStory, "Exec", 0),
            new("ZCOD", secondStory, "Exec", 0)
        ]));
        Assert.Equal(2, multipleExecutables.Executables.Length);
        Assert.Equal((byte)5, multipleExecutables.ZCodeStory!.Header.Version);

        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(BlorbFixtureBuilder.Create(
        [
            new("PNG ", [1], "Pict", 3),
            new("JPEG", [2], "Pict", 3)
        ])));
    }

    [Fact]
    public void ValidatesSingletonMetadataChunkLengthsAndUtf8()
    {
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("RelN", [1])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("Fspc", [1, 2])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("IFmd", [0xC3, 0x28])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("RelN", Word(1)), new("RelN", Word(2))])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([new("PNG ", [1], "Pict", 1)], [new("Fspc", Dword(2))])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([new("Rect", [0, 0, 0, 1, 0, 0, 0, 1], "Pict", 1)], [new("Fspc", Dword(1))])));
    }

    [Fact]
    public void ValidatesResourceDescriptionBoundsUtf8AndUniqueness()
    {
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("RDes", [0, 0, 0, 1, (byte)'P'])])));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("RDes", ResourceDescription("Pict", 1, "bad"))],
                malformedDescriptionUtf8: true)));
        Assert.Throws<BlorbFormatException>(() => BlorbFile.Parse(
            BlorbFixtureBuilder.Create([], [new("RDes", Join(
                ResourceDescription("Pict", 1, "one"), ResourceDescription("Pict", 1, "two")))])));
    }

    private static byte[] Word(ushort value)
    {
        var bytes = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Dword(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] ResourceDescription(string usage, uint number, string text)
    {
        var encoded = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[4 + 12 + encoded.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 1);
        Encoding.ASCII.GetBytes(usage).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), number);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), checked((uint)encoded.Length));
        encoded.CopyTo(bytes, 16);
        return bytes;
    }

    private static byte[] Join(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length - 4];
        BinaryPrimitives.WriteUInt32BigEndian(result, 2);
        first.AsSpan(4).CopyTo(result.AsSpan(4));
        second.AsSpan(4).CopyTo(result.AsSpan(first.Length));
        return result;
    }

    private sealed record FixtureChunk(string Type, byte[] Data, string? Usage = null, uint Number = 0);

    private static class BlorbFixtureBuilder
    {
        public static byte[] Create(
            FixtureChunk[] resources,
            FixtureChunk[]? optionalChunks = null,
            bool allowDuplicateIndex = false,
            bool malformedDescriptionUtf8 = false,
            bool indexFirst = true)
        {
            optionalChunks ??= [];
            if (malformedDescriptionUtf8)
            {
                var description = optionalChunks.Single(chunk => chunk.Type == "RDes");
                description.Data[^1] = 0xFF;
            }

            var indexData = new byte[4 + resources.Length * 12];
            BinaryPrimitives.WriteUInt32BigEndian(indexData, checked((uint)resources.Length));
            var indexChunk = new FixtureChunk("RIdx", indexData);
            var allChunks = allowDuplicateIndex
                ? new[] { indexChunk }.Concat(optionalChunks).Append(indexChunk).Concat(resources).ToArray()
                : indexFirst
                    ? new[] { indexChunk }.Concat(optionalChunks).Concat(resources).ToArray()
                    : optionalChunks.Concat(new[] { indexChunk }).Concat(resources).ToArray();
            var chunkOffset = 12;
            foreach (var chunk in allChunks)
            {
                if (chunk.Usage is not null)
                {
                    var resourceIndex = Array.IndexOf(resources, chunk);
                    var indexEntryOffset = 4 + resourceIndex * 12;
                    Encoding.ASCII.GetBytes(chunk.Usage).CopyTo(indexData, indexEntryOffset);
                    BinaryPrimitives.WriteUInt32BigEndian(indexData.AsSpan(indexEntryOffset + 4), chunk.Number);
                    BinaryPrimitives.WriteUInt32BigEndian(indexData.AsSpan(indexEntryOffset + 8), checked((uint)chunkOffset));
                }

                chunkOffset += 8 + chunk.Data.Length + (chunk.Data.Length & 1);
            }

            using var body = new MemoryStream();
            body.Write(Encoding.ASCII.GetBytes("IFRS"));
            Span<byte> length = stackalloc byte[4];
            foreach (var chunk in allChunks)
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
}
