using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineCallStackTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LoadsV1ToV4LocalDefaultsThenCopiesSuppliedArguments(byte version)
    {
        var story = MachineStoryFactory.Create(version);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, version, 0x400, 2, 0x1111, 0x2222);
        var callStack = new ZMachineCallStack(new ZMachineMemory(StoryLoader.Load(story)), StoryLoader.Load(story).Header);

        callStack.CallRoutine(packedAddress, [0xaaaa], 0x220, storeVariable: 17);

        Assert.Equal(0x400 + 5, callStack.ProgramCounter);
        Assert.Equal(0xaaaa, callStack.GetLocal(1));
        Assert.Equal(0x2222, callStack.GetLocal(2));
        Assert.Equal(1, callStack.CurrentArgumentCount);
    }

    [Fact]
    public void InitializesV5AndLaterLocalsToZeroAndDiscardsExcessArguments()
    {
        var story = MachineStoryFactory.Create(5);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, 5, 0x400, 2);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        callStack.CallRoutine(packedAddress, [0x1111, 0x2222, 0x3333], 0x220);

        Assert.Equal(0x401, callStack.ProgramCounter);
        Assert.Equal(0x1111, callStack.GetLocal(1));
        Assert.Equal(0x2222, callStack.GetLocal(2));
        Assert.Equal(3, callStack.CurrentArgumentCount);
        Assert.True(callStack.HasArgument(3));
        Assert.False(callStack.HasArgument(4));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    public void EnforcesVersionSpecificRoutineArgumentLimits(byte version, int argumentCount)
    {
        var story = MachineStoryFactory.Create(version);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, version, 0x400, 0);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);
        var arguments = Enumerable.Repeat((ushort)1, argumentCount).ToArray();

        Assert.Throws<ZMachineRuntimeException>(() => callStack.CallRoutine(packedAddress, arguments, 0x220));
        Assert.Equal(1, callStack.FrameCount);
    }

    [Fact]
    public void Version4CallCanSupplySevenArguments()
    {
        var story = MachineStoryFactory.Create(4);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, 4, 0x400, 1);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        callStack.CallRoutine(packedAddress, Enumerable.Range(1, 7).Select(value => (ushort)value).ToArray(), 0x220);

        Assert.Equal(7, callStack.CurrentArgumentCount);
        Assert.Equal((ushort)1, callStack.GetLocal(1));
    }

    [Fact]
    public void CallToPackedAddressZeroReturnsFalseWithoutCreatingAFrame()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        var result = callStack.CallRoutine(0, [], 0x240, storeVariable: 0x10);

        Assert.False(result.RoutineStarted);
        Assert.Equal((ushort)0, result.ImmediateReturnValue);
        Assert.Equal((byte)0x10, result.StoreVariable);
        Assert.Equal(1, callStack.FrameCount);
        Assert.Equal(0x240, callStack.ProgramCounter);
    }

    [Fact]
    public void ReturnRestoresCallerPcAndDiscardsOnlyTheReturningRoutinesEvaluationValues()
    {
        var story = MachineStoryFactory.Create(5);
        var routineA = MachineStoryFactory.WriteRoutine(story, 5, 0x400, 1);
        var routineB = MachineStoryFactory.WriteRoutine(story, 5, 0x410, 0);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        callStack.Push(0x1111);
        callStack.CallRoutine(routineA, [], 0x220);
        callStack.SetLocal(1, 0xabcd);
        callStack.Push(0x2222);
        callStack.CallRoutine(routineB, [], 0x230, storeVariable: 0x20);
        callStack.Push(0x3333);

        var innerReturn = callStack.ReturnFromRoutine(0x4444);

        Assert.Equal(0x230, innerReturn.ResumeAddress);
        Assert.Equal((byte)0x20, innerReturn.StoreVariable);
        Assert.Equal(0x2222, callStack.Pop());
        Assert.Equal(0xabcd, callStack.GetLocal(1));

        callStack.ReturnFromRoutine(0x5555);

        Assert.Equal(0x220, callStack.ProgramCounter);
        Assert.Equal(0x1111, callStack.Pop());
        Assert.Equal(1, callStack.FrameCount);
        Assert.Equal(0, callStack.EvaluationStackDepth);
    }

    [Fact]
    public void ReturnPoppedUsesTheCurrentRoutineEvaluationStack()
    {
        var story = MachineStoryFactory.Create(5);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, 5, 0x400, 0);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);
        callStack.CallRoutine(packedAddress, [], 0x220);
        callStack.Push(0x9876);

        var result = callStack.ReturnPopped();

        Assert.Equal((ushort)0x9876, result.Value);
        Assert.Equal(0x220, result.ResumeAddress);
        Assert.Equal(0, callStack.EvaluationStackDepth);
    }

    [Fact]
    public void CatchAndThrowUnwindToTheCaughtRoutineAndReturnItsValue()
    {
        var story = MachineStoryFactory.Create(5);
        var routineA = MachineStoryFactory.WriteRoutine(story, 5, 0x400, 0);
        var routineB = MachineStoryFactory.WriteRoutine(story, 5, 0x410, 0);
        var routineC = MachineStoryFactory.WriteRoutine(story, 5, 0x420, 0);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);
        callStack.CallRoutine(routineA, [], 0x220, storeVariable: 0x10);
        var caughtFrame = callStack.Catch();
        Assert.Equal((ushort)1, caughtFrame);
        callStack.CallRoutine(routineB, [], 0x230);
        callStack.CallRoutine(routineC, [], 0x240);
        callStack.Push(0xaaaa);

        var thrown = callStack.Throw(0xbeef, caughtFrame);

        Assert.Equal((ushort)0xbeef, thrown.Value);
        Assert.Equal(0x220, thrown.ResumeAddress);
        Assert.Equal((byte)0x10, thrown.StoreVariable);
        Assert.Equal(0x220, callStack.ProgramCounter);
        Assert.Equal(1, callStack.FrameCount);
        Assert.Equal(0, callStack.EvaluationStackDepth);
    }

    [Fact]
    public void RejectsReturnFromInitialEnvironmentAndUnknownThrowFrames()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        Assert.Throws<ZMachineRuntimeException>(() => callStack.ReturnFromRoutine(0));
        Assert.Throws<ZMachineRuntimeException>(() => callStack.Throw(0, 1));
    }

    [Fact]
    public void V6StartsByEnteringTheMainRoutineAndCannotReturnFromIt()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(6));
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        Assert.Equal(0x201, callStack.ProgramCounter);
        Assert.Equal(1, callStack.FrameCount);
        Assert.Throws<ZMachineRuntimeException>(() => callStack.ReturnFromRoutine(0));
    }

    [Fact]
    public void CatchCountsRoutineFramesAndExcludesTheNonV6TopLevelEnvironment()
    {
        var v5 = StoryLoader.Load(MachineStoryFactory.Create(5));
        var v5Stack = new ZMachineCallStack(new ZMachineMemory(v5), v5.Header);
        var routineAddress = MachineStoryFactory.WriteRoutine(MachineStoryFactory.Create(5), 5, 0x400, 0);

        Assert.Equal((ushort)0, v5Stack.Catch());
        v5Stack.CallRoutine(routineAddress, [], 0x220);
        Assert.Equal((ushort)1, v5Stack.Catch());

        var v6 = StoryLoader.Load(MachineStoryFactory.Create(6));
        var v6Stack = new ZMachineCallStack(new ZMachineMemory(v6), v6.Header);
        Assert.Equal((ushort)1, v6Stack.Catch());
    }

    [Fact]
    public void RejectsLocalsOutsideTheCurrentRoutine()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        Assert.Throws<ZMachineRuntimeException>(() => callStack.GetLocal(1));
        Assert.Throws<ZMachineRuntimeException>(() => callStack.SetLocal(0, 1));
        Assert.False(callStack.HasArgument(1));
    }

    [Fact]
    public void RejectsRoutineHeadersWithMoreThanFifteenLocals()
    {
        var story = MachineStoryFactory.Create(5);
        var packedAddress = MachineStoryFactory.WriteRoutine(story, 5, 0x400, 16);
        var loaded = StoryLoader.Load(story);
        var callStack = new ZMachineCallStack(new ZMachineMemory(loaded), loaded.Header);

        var error = Assert.Throws<ZMachineRuntimeException>(() => callStack.CallRoutine(packedAddress, [], 0x220));

        Assert.Contains("maximum is 15", error.Message);
        Assert.Equal(1, callStack.FrameCount);
    }
}
