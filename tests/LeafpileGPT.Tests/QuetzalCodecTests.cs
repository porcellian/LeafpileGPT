namespace LeafpileGPT.Tests;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using LeafpileGPT.Core;
using Xunit;

public sealed class QuetzalCodecTests
{
    [Theory]
    [InlineData(QuetzalMemoryEncoding.Compressed)]
    [InlineData(QuetzalMemoryEncoding.Uncompressed)]
    public void RoundTripsRequiredChunksFramesAndOptionalMetadata(QuetzalMemoryEncoding encoding)
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var save = CreateSave(story, 0x0123);
        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story, encoding), story);

        Assert.Equal(save.Version, decoded.Version);
        Assert.Equal(save.Identity.ReleaseNumber, decoded.Identity.ReleaseNumber);
        Assert.Equal(save.Identity.SerialNumber.ToArray(), decoded.Identity.SerialNumber.ToArray());
        Assert.Equal(save.Identity.Checksum, decoded.Identity.Checksum);
        Assert.Equal(save.Identity.ProgramCounter, decoded.Identity.ProgramCounter);
        Assert.Equal(save.DynamicMemory.ToArray(), decoded.DynamicMemory.ToArray());
        Assert.Equal(save.StackFrames.Length, decoded.StackFrames.Length);
        Assert.Equal(save.StackFrames[0].EvaluationStack.ToArray(), decoded.StackFrames[0].EvaluationStack.ToArray());
        Assert.Equal(save.StackFrames[1].Locals.ToArray(), decoded.StackFrames[1].Locals.ToArray());
        Assert.Equal(save.StackFrames[1].EvaluationStack.ToArray(), decoded.StackFrames[1].EvaluationStack.ToArray());
        Assert.Equal(save.Annotations.SelectMany(chunk => chunk.Data).ToArray(),
            decoded.Annotations.SelectMany(chunk => chunk.Data).ToArray());
        Assert.Equal(save.InterpreterData[0].Data.ToArray(), decoded.InterpreterData[0].Data.ToArray());
        Assert.Contains(decoded.AdditionalChunks, chunk => chunk.Type == "XTRA"
            && chunk.Data.SequenceEqual(new byte[] { 0xCA, 0xFE }));
    }

    [Fact]
    public void UsesThreeByteProgramCountersAndPreservesIfhdExtensionData()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(8, length: 131_072));
        var save = CreateSave(story, 0x01ABCD) with
        {
            Identity = QuetzalStoryIdentity.FromStory(story, 0x01ABCD) with { ExtensionData = [0xCA, 0xFE] }
        };

        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story), story);

        Assert.Equal(0x01ABCDu, decoded.Identity.ProgramCounter);
        Assert.Equal(new byte[] { 0xCA, 0xFE }, decoded.Identity.ExtensionData);
    }

    [Fact]
    public void HandlesNonV6DummyFrameAndV6RootFrameWithoutDummy()
    {
        var v5Story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var v5 = QuetzalCodec.Decode(QuetzalCodec.Encode(CreateSave(v5Story, 850), v5Story), v5Story);
        Assert.True(v5.StackFrames[0].IsDummy);
        Assert.Equal(new ushort[] { 0x1111 }, v5.StackFrames[0].EvaluationStack);

        var v6Bytes = StoryFixtureBuilder.Create(6);
        v6Bytes[800] = 0;
        StoryFixtureBuilder.WriteWord(v6Bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(v6Bytes));
        var v6Story = StoryFile.Parse(v6Bytes);
        var v6 = QuetzalCodec.Decode(QuetzalCodec.Encode(CreateSave(v6Story, 801), v6Story), v6Story);
        Assert.False(v6.StackFrames[0].IsDummy);
        Assert.Equal(new ushort[] { 0, 0 }, v6.StackFrames[0].Locals);
    }

    [Fact]
    public void LegacyVersionThreeIfhdUsesCalculatedChecksum()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(3, includeLengthAndChecksum: false));
        var save = CreateSave(story, 850);

        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story), story);

        Assert.Equal(StoryFixtureBuilder.CalculateChecksum(story.Bytes.Span), decoded.Identity.Checksum);
        Assert.True(decoded.StackFrames[0].IsDummy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RoundTripsFramesForEveryZMachineVersion(byte version)
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(version,
            includeLengthAndChecksum: version != 3));
        var save = CreateSave(story, version == 6 ? 801u : 850u);

        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story), story);

        Assert.Equal(version, decoded.Version);
        Assert.Equal(version != 6, decoded.StackFrames[0].IsDummy);
        Assert.Equal(save.StackFrames.Length, decoded.StackFrames.Length);
    }

    [Fact]
    public void CompressedMemoryUsesXorRunsAndAcceptsOmittedTrailingEqualBytes()
    {
        byte[] original = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] current = [1, 0xFF, 3, 4, 0, 0, 7, 8];
        var encoded = QuetzalCodec.EncodeCompressedMemory(current, original);

        Assert.Equal(new byte[] { 0, 0, 0xFD, 0, 1, 5, 6, 0, 1 }, encoded);
        Assert.Equal(current, QuetzalCodec.DecodeCompressedMemory(encoded, original));
        Assert.Equal(original, QuetzalCodec.DecodeCompressedMemory([], original));
    }

    [Fact]
    public void CompressedMemoryHandlesMaximumZeroRunAndRejectsMalformedRuns()
    {
        var zeros = new byte[256];
        Assert.Equal(new byte[] { 0, 0xFF }, QuetzalCodec.EncodeCompressedMemory(zeros, zeros));
        Assert.Equal(zeros, QuetzalCodec.DecodeCompressedMemory([0, 0xFF], zeros));

        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.DecodeCompressedMemory([0], [0]));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.DecodeCompressedMemory([0, 1], [0]));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.DecodeCompressedMemory([1, 1], [0]));
        Assert.Throws<ArgumentException>(() => QuetzalCodec.EncodeCompressedMemory([1], [1, 2]));
    }

    [Fact]
    public void UncompressedMemoryRequiresExactDynamicMemoryLength()
    {
        Assert.Equal(new byte[] { 1, 2 }, QuetzalCodec.DecodeUncompressedMemory([1, 2], 2));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.DecodeUncompressedMemory([1], 2));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.DecodeUncompressedMemory([1, 2, 3], 2));
    }

    [Fact]
    public void StackCodecPreservesFlagsArgumentMaskLocalsAndEvaluationWords()
    {
        var frame = new QuetzalStackFrame(0x010203, false, 16, 0b0100101, [0xAAAA, 0xBBBB], [1, 2, 3]);

        var encoded = QuetzalCodec.EncodeStackFrames(5, [DummyFrame([0x9999]), frame]);
        var decoded = QuetzalCodec.DecodeStackFrames(encoded, 5);

        Assert.True(decoded[0].IsDummy);
        Assert.Equal(frame.ReturnProgramCounter, decoded[1].ReturnProgramCounter);
        Assert.Equal(frame.DiscardsResult, decoded[1].DiscardsResult);
        Assert.Equal(frame.StoreVariable, decoded[1].StoreVariable);
        Assert.Equal(frame.ArgumentMask, decoded[1].ArgumentMask);
        Assert.Equal(frame.Locals.ToArray(), decoded[1].Locals.ToArray());
        Assert.Equal(frame.EvaluationStack.ToArray(), decoded[1].EvaluationStack.ToArray());
    }

    [Fact]
    public void ParsesIntDAndPreservesAnnotationsUnknownAndDuplicateOptionalChunks()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var intd = QuetzalCodec.CreateInterpreterData(new QuetzalInterpreterData(
            "UNIX", true, false, 7, "JZIP", [1, 2, 3]));
        var save = CreateSave(story, 850) with
        {
            AdditionalChunks =
            [
                new("ANNO", Encoding.ASCII.GetBytes("First note").ToImmutableArray()),
                new("ANNO", Encoding.ASCII.GetBytes("Second note").ToImmutableArray()),
                intd,
                new("XTRA", [0xCA, 0xFE])
            ]
        };

        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story), story);

        Assert.Equal(2, decoded.Annotations.Length);
        var parsedIntd = Assert.Single(decoded.InterpreterData);
        Assert.Equal("UNIX", parsedIntd.OperatingSystemId);
        Assert.True(parsedIntd.SameMachineOnly);
        Assert.False(parsedIntd.CurrentStateOnly);
        Assert.Equal("JZIP", parsedIntd.InterpreterId);
        Assert.Equal(new byte[] { 1, 2, 3 }, parsedIntd.Data);
        Assert.Equal(2, decoded.AdditionalChunks.Count(chunk => chunk.Type == "ANNO"));
    }

    [Fact]
    public void IgnoresLaterDuplicateRequiredChunkAndPreservesUnknownChunk()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var encoded = QuetzalCodec.Encode(CreateSave(story, 850), story);
        var duplicateIfhd = ReadRawChunk(encoded, FindChunk(encoded, "IFhd"), out _);
        var withDuplicate = AppendRawChunk(encoded, duplicateIfhd);

        var decoded = QuetzalCodec.Decode(withDuplicate, story);

        Assert.Equal(850u, decoded.Identity.ProgramCounter);
        Assert.Contains(decoded.AdditionalChunks, chunk => chunk.Type == "XTRA");
    }

    [Fact]
    public void RejectsDifferentStoryIdentityAndInvalidStackShapes()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var otherBytes = StoryFixtureBuilder.Create(5);
        otherBytes[0x12]++;
        StoryFixtureBuilder.WriteWord(otherBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(otherBytes));
        var otherStory = StoryFile.Parse(otherBytes);
        var encoded = QuetzalCodec.Encode(CreateSave(story, 850), story);

        Assert.Throws<QuetzalStoryMismatchException>(() => QuetzalCodec.Decode(encoded, otherStory));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.Encode(
            CreateSave(story, 850) with { StackFrames = [new(0, false, 0, 0, [], [], false)] }, story));
        Assert.Throws<QuetzalFormatException>(() => QuetzalCodec.Encode(
            CreateSave(story, 850) with { StackFrames = [DummyFrame([]), new(1, true, 1, 0, [], [], false)] }, story));
    }

    [Theory]
    [InlineData("wrong-form")]
    [InlineData("bad-length")]
    [InlineData("bad-pad")]
    [InlineData("missing-ifhd")]
    [InlineData("missing-memory")]
    [InlineData("wrong-order")]
    [InlineData("invalid-pc")]
    [InlineData("truncated-stks")]
    [InlineData("invalid-intd")]
    [InlineData("invalid-annotation")]
    public void RejectsMalformedIfzsStructures(string malformedCase)
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
        var valid = QuetzalCodec.Encode(CreateSave(story, 850), story);
        var malformed = MutateIfzs(valid, malformedCase);

        Assert.ThrowsAny<QuetzalFormatException>(() => QuetzalCodec.Decode(malformed, story));
    }

    private static QuetzalSaveData CreateSave(StoryFile story, uint pc)
    {
        var memory = story.Bytes[..story.Header.StaticMemoryBase].ToArray();
        memory[100] ^= 0xFF;
        memory[300] = 0xAA;
        var frames = story.Header.Version == 6
            ? ImmutableArray.Create(new QuetzalStackFrame(0, true, 0, 0, [0, 0], [0x1111]))
            : ImmutableArray.Create(DummyFrame([0x1111]),
                new QuetzalStackFrame(900, false, 16, 0b0000011, [0xAAAA], [0x2222, 0x3333]));
        var intd = QuetzalCodec.CreateInterpreterData(new QuetzalInterpreterData(
            "UNIX", false, false, 0, "TEST", [0x10, 0x20]));
        return new QuetzalSaveData(story.Header.Version, QuetzalStoryIdentity.FromStory(story, pc),
            memory.ToImmutableArray(), frames,
            [new("ANNO", Encoding.ASCII.GetBytes("test").ToImmutableArray()), intd, new("XTRA", [0xCA, 0xFE])]);
    }

    private static QuetzalStackFrame DummyFrame(ImmutableArray<ushort> evaluationStack) =>
        new(0, false, 0, 0, [], evaluationStack, true);

    private static byte[] MutateIfzs(byte[] source, string malformedCase)
    {
        var bytes = source.ToArray();
        var stksOffset = FindChunk(bytes, "Stks");
        switch (malformedCase)
        {
            case "wrong-form":
                Encoding.ASCII.GetBytes("NOPE").CopyTo(bytes, 0);
                break;
            case "bad-length":
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)(bytes.Length - 7));
                break;
            case "bad-pad":
                {
                    var annoOffset = FindChunk(bytes, "ANNO");
                    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(annoOffset + 4), 3);
                    bytes[annoOffset + 8 + 3] = 1;
                    break;
                }
            case "missing-ifhd":
                {
                    var ifhdOffset = FindChunk(bytes, "IFhd");
                    bytes.AsSpan(ifhdOffset, 8 + 14).Clear();
                    break;
                }
            case "missing-memory":
                Encoding.ASCII.GetBytes("JUNK").CopyTo(bytes, FindChunk(bytes, "CMem"));
                break;
            case "invalid-pc":
                {
                    var ifhdOffset = FindChunk(bytes, "IFhd");
                    bytes[ifhdOffset + 8 + 10] = 0;
                    bytes[ifhdOffset + 8 + 11] = 0x04;
                    bytes[ifhdOffset + 8 + 12] = 0;
                    break;
                }
            case "wrong-order":
                return SwapFirstTwoChunks(bytes);
            case "truncated-stks":
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(stksOffset + 4), 1);
                bytes[stksOffset + 8] = 0;
                break;
            case "invalid-intd":
                {
                    var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));
                    var validIntd = QuetzalCodec.CreateInterpreterData(new QuetzalInterpreterData(
                        "UNIX", false, false, 0, "TEST", [1]));
                    var invalid = CreateSave(story, 850) with
                    {
                        AdditionalChunks = [validIntd]
                    };
                    var intdBytes = QuetzalCodec.Encode(invalid, story);
                    var intdOffset = FindChunk(intdBytes, "IntD");
                    intdBytes[intdOffset + 8 + 4] = 0x80;
                    return intdBytes;
                }
            case "invalid-annotation":
                {
                    var annoOffset = FindChunk(bytes, "ANNO");
                    bytes[annoOffset + 8] = 0x01;
                    break;
                }
        }

        return bytes;
    }

    private static int FindChunk(ReadOnlySpan<byte> data, string type)
    {
        var offset = 12;
        while (offset + 8 <= data.Length)
        {
            var chunkType = Encoding.ASCII.GetString(data.Slice(offset, 4));
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
            if (chunkType == type)
            {
                return offset;
            }

            offset = checked(offset + 8 + (int)length + (int)(length & 1));
        }

        throw new InvalidOperationException($"Fixture chunk '{type}' was not found.");
    }

    private static byte[] SwapFirstTwoChunks(ReadOnlySpan<byte> form)
    {
        var first = ReadRawChunk(form, 12, out var firstEnd);
        var second = ReadRawChunk(form, firstEnd, out var secondEnd);
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("FORM"));
        output.Write(new byte[4]);
        output.Write(Encoding.ASCII.GetBytes("IFZS"));
        output.Write(second);
        output.Write(first);
        output.Write(form[secondEnd..]);
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }

    private static byte[] ReadRawChunk(ReadOnlySpan<byte> data, int start, out int end)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(data[(start + 4)..]);
        end = checked(start + 8 + (int)length + (int)(length & 1));
        return data[start..end].ToArray();
    }

    private static byte[] AppendRawChunk(ReadOnlySpan<byte> form, ReadOnlySpan<byte> rawChunk)
    {
        var result = new byte[form.Length + rawChunk.Length];
        form.CopyTo(result);
        rawChunk.CopyTo(result.AsSpan(form.Length));
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), checked((uint)(result.Length - 8)));
        return result;
    }
}
