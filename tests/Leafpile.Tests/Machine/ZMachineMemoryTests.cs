using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineMemoryTests
{
    [Fact]
    public void ReadsAndWritesDynamicBytesAndBigEndianWords()
    {
        var memory = new ZMachineMemory(StoryLoader.Load(MachineStoryFactory.Create(5)));

        memory.WriteByte(0x80, 0x7a);
        memory.WriteWord(0x100, 0x1234);

        Assert.Equal(0x7a, memory.ReadByte(0x80));
        Assert.Equal(0x1234, memory.ReadWord(0x100));
        Assert.Equal(0x12, memory.ReadByte(0x100));
        Assert.Equal(0x34, memory.ReadByte(0x101));
    }

    [Fact]
    public void RejectsWritesToStaticMemory()
    {
        var memory = new ZMachineMemory(StoryLoader.Load(MachineStoryFactory.Create(5)));

        var error = Assert.Throws<ZMachineRuntimeException>(() => memory.WriteByte(0x300, 0xff));

        Assert.Contains("outside dynamic memory", error.Message);
    }

    [Fact]
    public void RejectsWordWriteThatWouldCrossIntoStaticMemoryWithoutPartialMutation()
    {
        var memory = new ZMachineMemory(StoryLoader.Load(MachineStoryFactory.Create(5)));
        var before = memory.ReadByte(0x2ff);

        Assert.Throws<ZMachineRuntimeException>(() => memory.WriteWord(0x2ff, 0xabcd));

        Assert.Equal(before, memory.ReadByte(0x2ff));
    }

    [Fact]
    public void AllowsReadsThroughStaticMemoryButNotBeyondAddressableMemory()
    {
        var story = MachineStoryFactory.Create(5);
        story[0x300] = 0x44;
        var memory = new ZMachineMemory(StoryLoader.Load(story));

        Assert.Equal(0x44, memory.ReadByte(0x300));
        Assert.Throws<ZMachineRuntimeException>(() => memory.ReadByte(0x400));
    }

    [Fact]
    public void ReadsInstructionBytesFromHighMemory()
    {
        var story = MachineStoryFactory.Create(8, 0x20000);
        story[0x1ffff] = 0x5a;
        var memory = new ZMachineMemory(StoryLoader.Load(story));

        Assert.Equal(0x5a, memory.ReadInstructionByte(0x1ffff));
    }

    [Fact]
    public void RejectsInstructionFetchOutsideTheStoryImage()
    {
        var memory = new ZMachineMemory(StoryLoader.Load(MachineStoryFactory.Create(5)));

        Assert.Throws<ZMachineRuntimeException>(() => memory.ReadInstructionByte(-1));
        Assert.Throws<ZMachineRuntimeException>(() => memory.ReadInstructionByte(memory.StoryLength));
    }
}
