using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineUserStackTests
{
    [Fact]
    public void PushAndPullUseTheAvailableSlotCountAndLifoOrder()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(6));
        var memory = new ZMachineMemory(loaded);
        var callStack = new ZMachineCallStack(memory, loaded.Header);
        memory.WriteWord(0x150, 2);

        Assert.True(callStack.PushUserStack(0xaaaa, 0x150));
        Assert.Equal(1, memory.ReadWord(0x150));
        Assert.True(callStack.PushUserStack(0xbbbb, 0x150));
        Assert.Equal(0, memory.ReadWord(0x150));
        Assert.False(callStack.PushUserStack(0xcccc, 0x150));

        Assert.Equal((ushort)0xbbbb, callStack.PullUserStack(0x150));
        Assert.Equal(1, memory.ReadWord(0x150));
        Assert.Equal((ushort)0xaaaa, callStack.PullUserStack(0x150));
        Assert.Equal(2, memory.ReadWord(0x150));
    }

    [Fact]
    public void PopStackCanDiscardValuesFromEitherTheGameOrAUserStack()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(6));
        var memory = new ZMachineMemory(loaded);
        var callStack = new ZMachineCallStack(memory, loaded.Header);
        memory.WriteWord(0x150, 3);
        callStack.PushUserStack(0x1111, 0x150);
        callStack.PushUserStack(0x2222, 0x150);
        callStack.Push(0xaaaa);
        callStack.Push(0xbbbb);

        callStack.PopStack(1);
        callStack.PopStack(1, 0x150);

        Assert.Equal((ushort)0xaaaa, callStack.Pop());
        Assert.Equal(2, memory.ReadWord(0x150));
    }

    [Fact]
    public void RejectsUserStackOperationsOutsideVersion6()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        Assert.Throws<ZMachineRuntimeException>(() => callStack.PushUserStack(1, 0x150));
        Assert.Throws<ZMachineRuntimeException>(() => callStack.PullUserStack(0x150));
    }

    [Fact]
    public void RejectsGameStackUnderflowAndOutOfRangeUserStackStorage()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(6));
        var memory = new ZMachineMemory(loaded);
        var callStack = new ZMachineCallStack(memory, loaded.Header);
        memory.WriteWord(0x2fe, 1);

        Assert.Throws<ZMachineRuntimeException>(() => callStack.Pop());
        Assert.Throws<ZMachineRuntimeException>(() => callStack.PushUserStack(1, 0x2fe));
        Assert.Throws<ZMachineRuntimeException>(() => callStack.PushUserStack(1, 0x300));
    }
}
