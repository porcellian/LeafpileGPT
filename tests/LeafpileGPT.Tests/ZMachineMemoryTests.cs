using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public class ZMachineMemoryTests
{
    [Fact]
    public void WritesAndReadsDynamicMemoryInBigEndianOrder()
    {
        var memory = CreateMemory();

        memory.WriteByte(64, 0xAB);
        memory.WriteWord(698, 0x1234);

        Assert.Equal((byte)0xAB, memory.ReadByte(64));
        Assert.Equal((ushort)0x1234, memory.ReadWord(698));
        Assert.Equal(ZMachineMemoryRegion.Dynamic, memory.GetRegion(699));
    }

    [Fact]
    public void RejectsWritesOutsideDynamicMemory()
    {
        var memory = CreateMemory();

        var byteException = Assert.Throws<ZMachineMemoryAccessException>(() => memory.WriteByte(700, 1));
        var wordException = Assert.Throws<ZMachineMemoryAccessException>(() => memory.WriteWord(699, 0x1234));

        Assert.Equal(700u, byteException.Address);
        Assert.Equal("write", byteException.Operation);
        Assert.Equal(699u, wordException.Address);
    }

    [Fact]
    public void ReadsStaticBytesAndStaticHighOverlapButProtectsThemFromWrites()
    {
        var memory = CreateMemory();

        Assert.Equal((byte)0xBC, memory.ReadByte(750));
        Assert.Equal((byte)0x20, memory.ReadByte(800));
        Assert.Equal(ZMachineMemoryRegion.Static, memory.GetRegion(750));
        Assert.Equal(ZMachineMemoryRegion.StaticAndHigh, memory.GetRegion(800));
        Assert.Throws<ZMachineMemoryAccessException>(() => memory.WriteByte(750, 1));
        Assert.Throws<ZMachineMemoryAccessException>(() => memory.WriteByte(800, 1));
    }

    [Fact]
    public void InternalReadsCanAddressHighMemoryBeyondSixteenBitByteAddressRange()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(8, length: 131_072));
        var memory = new ZMachineMemory(story);

        Assert.Equal((byte)(65_536 % 251), memory.ReadStoryByte(65_536));
        Assert.Equal(ZMachineMemoryRegion.High, memory.GetRegion(65_536));
        Assert.Throws<ZMachineMemoryAccessException>(() => memory.ReadStoryWord(131_071));
    }

    [Fact]
    public void RejectsDirectWordAddressCrossingSixteenBitBoundary()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(8, length: 131_072));
        var memory = new ZMachineMemory(story);

        Assert.Throws<ZMachineMemoryAccessException>(() => memory.ReadWord(ushort.MaxValue));
    }

    [Fact]
    public void RejectsDirectReadsOutsideLoadedStoryImage()
    {
        var memory = CreateMemory();

        var exception = Assert.Throws<ZMachineMemoryAccessException>(() => memory.ReadByte(1024));

        Assert.Equal(1024u, exception.Address);
        Assert.Equal("access", exception.Operation);
    }

    [Fact]
    public void MemoryWritesDoNotModifyValidatedStoryImage()
    {
        var data = StoryFixtureBuilder.Create(5);
        var story = StoryFile.Parse(data);
        var memory = new ZMachineMemory(story);

        memory.WriteByte(64, 0x55);

        Assert.Equal(data[64], story.Bytes.Span[64]);
        Assert.Equal((byte)0x55, memory.ReadByte(64));
    }

    [Fact]
    public void ReportsDynamicStaticAndHighMemoryBoundaries()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(8, length: 131_072));
        var memory = new ZMachineMemory(story);

        Assert.Equal((ushort)700, memory.DynamicMemoryEndExclusive);
        Assert.Equal((ushort)700, memory.StaticMemoryBase);
        Assert.Equal((ushort)800, memory.HighMemoryBase);
        Assert.Equal(ZMachineMemoryRegion.Dynamic, memory.GetRegion(699));
        Assert.Equal(ZMachineMemoryRegion.Static, memory.GetRegion(700));
        Assert.Equal(ZMachineMemoryRegion.StaticAndHigh, memory.GetRegion(800));
        Assert.Equal(ZMachineMemoryRegion.High, memory.GetRegion(65_536));
    }

    private static ZMachineMemory CreateMemory()
    {
        var data = StoryFixtureBuilder.Create(5);
        data[750] = 0xBC;
        data[800] = 0x20;
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));
        return new ZMachineMemory(StoryFile.Parse(data));
    }
}
