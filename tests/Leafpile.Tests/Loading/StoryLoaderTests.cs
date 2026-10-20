using System.Buffers.Binary;
using Leafpile.Core.Loading;

namespace Leafpile.Tests.Loading;

public sealed class StoryLoaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void LoadsStandaloneStoryForEachSupportedVersion(byte version)
    {
        var story = MakeStory(version);

        var loaded = StoryLoader.Load(story);

        Assert.Equal(story.Length, loaded.Data.Length);
        Assert.Equal(version, loaded.Header.Version);
        Assert.Equal(StorySourceKind.ZCodeFile, loaded.SourceKind);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    public void UsesVersionSpecificDeclaredLengthAndIgnoresTrailingBytes(byte version, int multiplier)
    {
        var story = MakeStory(version, 1024);
        var declaredLength = 1024 + (multiplier * 7);
        Array.Resize(ref story, declaredLength + 13);
        WriteWord(story, 0x1a, (ushort)(declaredLength / multiplier));

        var loaded = StoryLoader.Load(story);

        Assert.Equal(declaredLength, loaded.Data.Length);
        Assert.Equal(declaredLength, loaded.Header.FileLength);
    }

    [Fact]
    public void V3WithoutDeclaredLengthUsesPhysicalLength()
    {
        var story = MakeStory(3);
        WriteWord(story, 0x1a, 0);

        var loaded = StoryLoader.Load(story);

        Assert.Equal(story.Length, loaded.Data.Length);
    }

    [Fact]
    public void AllowsHighMemoryToBeginAtTheEndOfTheStory()
    {
        var story = MakeStory(5);
        WriteWord(story, 0x04, (ushort)story.Length);
        WriteWord(story, 0x08, 0x0300);

        var loaded = StoryLoader.Load(story);

        Assert.Equal(story.Length, loaded.Header.HighMemoryBase);
    }

    [Fact]
    public void ResolvesV6MainRoutineUsingRoutineOffset()
    {
        var story = MakeStory(6);
        WriteWord(story, 0x06, 0x80);
        WriteWord(story, 0x28, 0x10);
        story[0x80 * 4 + 0x10 * 8] = 2;

        var loaded = StoryLoader.Load(story);

        Assert.Equal(0x280, loaded.Header.InitialProgramAddress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(255)]
    public void RejectsUnsupportedVersions(byte version)
    {
        var story = MakeStory(5);
        story[0] = version;

        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(story));

        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(new byte[63]));

        Assert.Contains("header", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDeclaredLengthLongerThanAvailableBytes()
    {
        var story = MakeStory(5);
        WriteWord(story, 0x1a, 0x0101);

        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(story));

        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x0e, 0x20)]
    [InlineData(0x04, 0x2ff)]
    [InlineData(0x08, 0x0400)]
    [InlineData(0x0a, 0x02c0)]
    [InlineData(0x0c, 0x0200)]
    [InlineData(0x18, 0x03c0)]
    public void RejectsInvalidRequiredHeaderAddresses(int offset, ushort value)
    {
        var story = MakeStory(5);
        WriteWord(story, offset, value);

        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(story));

        Assert.Contains("Invalid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsV6MainRoutineWithTooManyLocals()
    {
        var story = MakeStory(6);
        story[0x200] = 16;

        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(story));

        Assert.Contains("local variable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadsExecutableFromBlorbResourceIndex()
    {
        var story = MakeStory(5);
        var blorb = MakeBlorb(story);

        var loaded = StoryLoader.Load(blorb);

        Assert.Equal(story, loaded.Data.ToArray());
        Assert.Equal(StorySourceKind.BlorbExecutable, loaded.SourceKind);
    }

    [Fact]
    public void RejectsBlorbWithoutResourceIndex()
    {
        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(MakeBlorbWithoutIndex()));

        Assert.Contains("RIdx", error.Message);
    }

    [Fact]
    public void RejectsBlorbWithoutExecutableEntry()
    {
        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(MakeBlorb(MakeStory(5), usage: "Pict")));

        Assert.Contains("executable", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsBlorbIndexPointingToNoChunk()
    {
        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(MakeBlorb(MakeStory(5), executableOffset: 0x7fffffff)));

        Assert.Contains("refers to no chunk", error.Message);
    }

    [Fact]
    public void RejectsBlorbExecutableOfWrongChunkType()
    {
        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(MakeBlorb(MakeStory(5), executableChunkId: "TEXT")));

        Assert.Contains("not ZCOD", error.Message);
    }

    [Fact]
    public void RejectsTruncatedBlorbChunk()
    {
        var blorb = MakeBlorb(MakeStory(5));
        Array.Resize(ref blorb, blorb.Length - 1);
        WriteUInt32(blorb, 4, (uint)(blorb.Length - 8));

        var error = Assert.Throws<StoryLoadException>(() => StoryLoader.Load(blorb));

        Assert.Contains("extends beyond", error.Message);
    }

    private static byte[] MakeStory(byte version, int length = 1024)
    {
        var story = new byte[length];
        story[0] = version;
        WriteWord(story, 0x04, 0x0300);
        WriteWord(story, 0x06, version == 6 ? (ushort)0x80 : (ushort)0x0200);
        WriteWord(story, 0x08, 0x0300);
        WriteWord(story, 0x0a, 0x0040);
        WriteWord(story, 0x0c, 0x0040);
        WriteWord(story, 0x0e, 0x0300);
        if (version >= 2)
        {
            WriteWord(story, 0x18, 0x0100);
        }

        if (version >= 3)
        {
            var multiplier = version <= 3 ? 2 : version <= 5 ? 4 : 8;
            WriteWord(story, 0x1a, (ushort)(length / multiplier));
            WriteWord(story, 0x1c, 0x1234);
        }

        if (version == 6)
        {
            story[0x200] = 0;
        }

        return story;
    }

    private static byte[] MakeBlorb(byte[] story, string usage = "Exec", uint? executableOffset = null, string executableChunkId = "ZCOD")
    {
        const int resourceIndexOffset = 12;
        const int executableChunkDataOffset = resourceIndexOffset + 8 + 16;
        var executableChunkOffset = executableOffset ?? executableChunkDataOffset;
        var formLength = 4 + 8 + 16 + 8 + story.Length;
        var blorb = new byte[8 + formLength];
        WriteFourCc(blorb, 0, "FORM");
        WriteUInt32(blorb, 4, (uint)formLength);
        WriteFourCc(blorb, 8, "IFRS");
        WriteFourCc(blorb, resourceIndexOffset, "RIdx");
        WriteUInt32(blorb, resourceIndexOffset + 4, 16);
        WriteUInt32(blorb, resourceIndexOffset + 8, 1);
        WriteFourCc(blorb, resourceIndexOffset + 12, usage);
        WriteUInt32(blorb, resourceIndexOffset + 16, 0);
        WriteUInt32(blorb, resourceIndexOffset + 20, executableChunkOffset);
        WriteFourCc(blorb, executableChunkDataOffset, executableChunkId);
        WriteUInt32(blorb, executableChunkDataOffset + 4, (uint)story.Length);
        story.CopyTo(blorb, executableChunkDataOffset + 8);
        return blorb;
    }

    private static byte[] MakeBlorbWithoutIndex()
    {
        const int chunkOffset = 12;
        var blorb = new byte[chunkOffset + 8];
        WriteFourCc(blorb, 0, "FORM");
        WriteUInt32(blorb, 4, (uint)(blorb.Length - 8));
        WriteFourCc(blorb, 8, "IFRS");
        WriteFourCc(blorb, chunkOffset, "JUNK");
        return blorb;
    }

    private static void WriteFourCc(byte[] data, int offset, string value) =>
        System.Text.Encoding.ASCII.GetBytes(value).CopyTo(data, offset);

    private static void WriteWord(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
}
