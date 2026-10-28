namespace LeafpileGPT.Tests;

using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineExecutionStateTests
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
    public void StartsAtCorrectInitialExecutionAddressForEveryVersion(byte version)
    {
        var state = Start(version);

        Assert.Equal(version == 6 ? 801u : 850u, state.ProgramCounter);
        Assert.True(state.CurrentFrame.IsRoot);
    }

    [Fact]
    public void V6StartsByCallingPackedMainRoutineWithZeroInitializedLocals()
    {
        var state = Start(6);

        Assert.Equal(800u, state.CurrentFrame.RoutineAddress);
        Assert.Equal(801u, state.ProgramCounter);
        Assert.Equal(new ushort[] { 0, 0 }, state.CurrentFrame.Locals);
        Assert.True(state.CurrentFrame.IsRoot);
        Assert.Null(state.CurrentFrame.ReturnProgramCounter);
    }

    [Fact]
    public void V6StartupAndRoutineCallsUseTheRoutineOffset()
    {
        var storyBytes = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(storyBytes, 0x28, 5);
        storyBytes[840] = 0;
        storyBytes[440] = 1;
        StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        var state = ZMachineExecutionState.Start(StoryFile.Parse(storyBytes));

        Assert.Equal(841u, state.ProgramCounter);
        state.CallRoutine(100, [], 860, null);

        Assert.Equal(441u, state.ProgramCounter);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(2, 300)]
    [InlineData(3, 300)]
    [InlineData(4, 150)]
    [InlineData(5, 150)]
    [InlineData(7, 150)]
    [InlineData(8, 75)]
    public void CallsPackedRoutineUsingVersionScale(byte version, ushort packedAddress)
    {
        var state = ZMachineExecutionState.Start(WithRoutine(version, 600, []));

        state.CallRoutine(packedAddress, [], 860, null);

        Assert.Equal(601u, state.ProgramCounter);
        Assert.Equal(600u, state.CurrentFrame.RoutineAddress);
    }

    [Fact]
    public void StackVariableReadsPopAndWritesPush()
    {
        var state = Start(5);

        state.WriteVariable(0, 0x1234);
        state.WriteVariable(0, 0x5678);

        Assert.Equal((ushort)0x5678, state.ReadVariable(0));
        Assert.Equal((ushort)0x1234, state.ReadVariable(0));
        Assert.Empty(state.EvaluationStack);
    }

    [Fact]
    public void IndirectStackVariableReadsAndWritesInPlace()
    {
        var state = Start(5);
        state.PushEvaluationStack(0x1234);

        Assert.Equal((ushort)0x1234, state.ReadVariableReference(0));
        state.WriteVariableReference(0, 0xABCD);

        Assert.Equal(new ushort[] { 0xABCD }, state.EvaluationStack);
    }

    [Fact]
    public void IndirectStackReferenceCannotReachCallerValues()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, [], localCount: 0));
        state.PushEvaluationStack(0x1234);
        state.CallRoutine(150, [], 860, null);

        Assert.Throws<ZMachineExecutionException>(() => state.ReadVariableReference(0));
        Assert.Throws<ZMachineExecutionException>(() => state.WriteVariableReference(0, 0x5678));
    }

    [Fact]
    public void LocalsAndGlobalsAreAddressedSeparately()
    {
        var story = WithRoutine(3, 600, [0x1234, 0x5678]);
        var state = ZMachineExecutionState.Start(story);
        state.CallRoutine(300, [], 860, null);

        Assert.Equal((ushort)0x1234, state.ReadVariable(1));
        Assert.Equal((ushort)0x5678, state.ReadVariable(2));
        state.WriteVariable(16, 0xABCD);
        Assert.Equal((ushort)0xABCD, state.Memory.ReadWord(story.Header.GlobalsTableAddress));
        Assert.Equal((ushort)0xABCD, state.ReadVariable(16));
    }

    [Fact]
    public void V5LocalsDefaultToZeroAndArgumentsReplaceInitialValues()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, [], localCount: 4));
        ushort[] arguments = [0x1111, 0x2222];

        state.CallRoutine(150, arguments, 860, null);

        Assert.Equal(new ushort[] { 0x1111, 0x2222, 0, 0 }, state.CurrentFrame.Locals);
        Assert.Equal((byte)2, state.CurrentArgumentCount);
    }

    [Fact]
    public void ArgumentsBeyondLocalCountAreDiscardedButCountIsPreserved()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, [], localCount: 4));
        ushort[] arguments = [10, 20, 30];

        state.CallRoutine(150, arguments, 860, null);

        Assert.Equal(new ushort[] { 10, 20, 30, 0 }, state.CurrentFrame.Locals);
        Assert.True(state.HasArgument(3));
        Assert.False(state.HasArgument(4));
    }

    [Fact]
    public void V3RoutineArgumentsReplaceOnlyCorrespondingLegacyDefaults()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(3, 600, [0x1234, 0x5678]));
        ushort[] arguments = [0xAAAA];

        state.CallRoutine(300, arguments, 860, null);

        Assert.Equal(new ushort[] { 0xAAAA, 0x5678 }, state.CurrentFrame.Locals);
    }

    [Fact]
    public void RoutineReturnDiscardsItsEvaluationValuesAndStoresReturnValue()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, []));
        state.PushEvaluationStack(0x1111);
        state.CallRoutine(150, [], 860, 0);
        state.PushEvaluationStack(0x2222);

        state.ReturnFromRoutine(0x3333);

        Assert.Equal(860u, state.ProgramCounter);
        Assert.Equal(new ushort[] { 0x1111, 0x3333 }, state.EvaluationStack);
        Assert.Single(state.CallStack);
    }

    [Fact]
    public void RoutineReturnCanStoreToCallerLocalOrGlobal()
    {
        var story = WithRoutine(5, 600, []);
        var state = ZMachineExecutionState.Start(story);
        state.CallRoutine(150, [], 860, 16);

        state.ReturnFromRoutine(0xCAFE);

        Assert.Equal((ushort)0xCAFE, state.ReadVariable(16));
    }

    [Fact]
    public void CallingPackedAddressZeroReturnsFalseWithoutPushingAFrame()
    {
        var state = Start(5);
        state.PushEvaluationStack(0x1234);

        state.CallRoutine(0, [], 860, 0);

        Assert.Equal(860u, state.ProgramCounter);
        Assert.Equal((ushort)0, state.ReadVariable(0));
        Assert.Single(state.CallStack);
    }

    [Fact]
    public void EnforcesVersionSpecificArgumentLimits()
    {
        var v3 = Start(3);
        var v4 = Start(4);

        Assert.Throws<ZMachineExecutionException>(() => v3.CallRoutine(0, [1, 2, 3, 4], 860, null));
        Assert.Throws<ZMachineExecutionException>(() => v4.CallRoutine(0, [1, 2, 3, 4, 5, 6, 7, 8], 860, null));
    }

    [Fact]
    public void V4AndLaterCallsCanSupplySevenArguments()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(4, 600, []));

        state.CallRoutine(150, [1, 2, 3, 4, 5, 6, 7], 860, null);

        Assert.Equal((byte)7, state.CurrentArgumentCount);
        Assert.True(state.HasArgument(7));
    }

    [Fact]
    public void RejectsAccessToNonexistentLocals()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, [], localCount: 1));
        state.CallRoutine(150, [], 860, null);

        Assert.Throws<ZMachineExecutionException>(() => state.ReadVariable(2));
        Assert.Throws<ZMachineExecutionException>(() => state.WriteVariable(2, 1));
    }

    [Fact]
    public void RejectsInvalidRoutineLocalCountAndTruncatedLegacyHeader()
    {
        var tooManyLocalsBytes = StoryFixtureBuilder.Create(5);
        tooManyLocalsBytes[600] = 16;
        RecalculateChecksum(tooManyLocalsBytes);
        var tooManyState = ZMachineExecutionState.Start(StoryFile.Parse(tooManyLocalsBytes));

        var truncated = StoryFixtureBuilder.Create(3);
        truncated[1022] = 1;
        RecalculateChecksum(truncated);
        var truncatedState = ZMachineExecutionState.Start(StoryFile.Parse(truncated));

        Assert.Throws<ZMachineExecutionException>(() => tooManyState.CallRoutine(150, [], 860, null));
        Assert.Throws<ZMachineExecutionException>(() => truncatedState.CallRoutine(511, [], 860, null));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void ReturningFromInitialExecutionEnvironmentIsIllegal(byte version)
    {
        var state = Start(version);

        Assert.Throws<ZMachineExecutionException>(() => state.ReturnFromRoutine(1));
    }

    [Fact]
    public void CatchAndThrowRestoreTheCaughtRoutineAndReturnValue()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, []));
        state.CallRoutine(150, [], 860, 16);
        var caughtFrame = state.CatchFrame();
        state.CallRoutine(150, [], 610, null);
        state.PushEvaluationStack(0xAAAA);

        state.ThrowToFrame(caughtFrame, 0xBEEF);

        Assert.Equal(860u, state.ProgramCounter);
        Assert.Equal((ushort)0xBEEF, state.ReadVariable(16));
        Assert.Single(state.CallStack);
        Assert.Empty(state.EvaluationStack);
    }

    [Fact]
    public void CatchReturnsCurrentFrameIndexAndThrowCanTargetCurrentRoutine()
    {
        var state = ZMachineExecutionState.Start(WithRoutine(5, 600, []));
        state.CallRoutine(150, [], 860, 16);
        var frameIndex = state.CatchFrame();

        state.ThrowToFrame(frameIndex, 0x1234);

        Assert.Equal(860u, state.ProgramCounter);
        Assert.Equal((ushort)0x1234, state.ReadVariable(16));
        Assert.Single(state.CallStack);
    }

    [Fact]
    public void RejectsInvalidThrowFrameIndices()
    {
        var state = Start(5);

        Assert.Throws<ZMachineExecutionException>(() => state.ThrowToFrame(0, 1));
        Assert.Throws<ZMachineExecutionException>(() => state.ThrowToFrame(1, 1));
    }

    [Fact]
    public void CatchAndThrowAreUnavailableBeforeVersionFive()
    {
        var state = Start(4);

        Assert.Throws<ZMachineExecutionException>(() => state.CatchFrame());
        Assert.Throws<ZMachineExecutionException>(() => state.ThrowToFrame(1, 1));
    }

    [Fact]
    public void V6UserStackPushPullAndPopTrackSpareSlots()
    {
        var state = Start(6);
        state.Memory.WriteWord(300, 3);

        Assert.True(state.PushUserStack(0x1111, 300));
        Assert.True(state.PushUserStack(0x2222, 300));
        Assert.Equal((ushort)1, state.Memory.ReadWord(300));
        Assert.Equal((ushort)0x2222, state.PullStack(300));
        Assert.Equal((ushort)2, state.Memory.ReadWord(300));
        state.PopStack(1, 300);
        Assert.Equal((ushort)3, state.Memory.ReadWord(300));
        Assert.Equal((ushort)0x1111, state.Memory.ReadWord(306));
    }

    [Fact]
    public void V6UserStackPushFailsWithoutChangingFullStack()
    {
        var state = Start(6);
        state.Memory.WriteWord(300, 0);

        Assert.False(state.PushUserStack(0xABCD, 300));
        Assert.Equal((ushort)0, state.Memory.ReadWord(300));
    }

    [Fact]
    public void V6PullStackSelectsEvaluationStackWhenAddressIsZero()
    {
        var state = Start(6);
        state.PushEvaluationStack(0x1234);

        Assert.Equal((ushort)0x1234, state.PullStack(0));
        Assert.Empty(state.EvaluationStack);
    }

    [Fact]
    public void UserStackUnderflowFollowsTheStandardWithoutAnUnderflowGuard()
    {
        var state = Start(6);
        state.Memory.WriteWord(300, 1);

        var value = state.PullStack(300);

        Assert.Equal(state.Memory.ReadWord(304), value);
        Assert.Equal((ushort)2, state.Memory.ReadWord(300));
    }

    [Fact]
    public void UserStacksAreRejectedOutsideVersionSix()
    {
        var state = Start(5);

        Assert.Throws<ZMachineExecutionException>(() => state.PushUserStack(1, 300));
        Assert.Throws<ZMachineExecutionException>(() => state.PullStack(300));
        Assert.Throws<ZMachineExecutionException>(() => state.PopStack(1, 300));
    }

    [Fact]
    public void ReportsEvaluationStackUnderflowAndInvalidArgumentNumbers()
    {
        var state = Start(5);

        Assert.Throws<ZMachineExecutionException>(() => state.PopEvaluationStack());
        Assert.Throws<ZMachineExecutionException>(() => state.ReadVariable(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.HasArgument(0));
    }

    [Fact]
    public void RequiresTheStandardMinimumStackCapacity()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));

        Assert.Throws<ArgumentOutOfRangeException>(() => ZMachineExecutionState.Start(story, 1_023));
        Assert.Equal(ZMachineExecutionState.DefaultMaximumStackWords, ZMachineExecutionState.Start(story).MaximumStackWords);
    }

    [Fact]
    public void EnforcesConfiguredStackCapacityForFrameAndEvaluationWords()
    {
        var state = ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(5)), 1_024);

        for (var index = 0; index < 1_020; index++)
        {
            state.PushEvaluationStack((ushort)index);
        }

        Assert.Throws<ZMachineExecutionException>(() => state.PushEvaluationStack(1));
        Assert.Equal(1_020, state.EvaluationStack.Length);
    }

    private static ZMachineExecutionState Start(byte version)
    {
        var story = StoryFixtureBuilder.Create(version);
        if (version == 6)
        {
            story[800] = 2;
            story[801] = 0xAA;
            StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        }

        return ZMachineExecutionState.Start(StoryFile.Parse(story));
    }

    private static StoryFile WithRoutine(byte version, int routineAddress, ushort[] localDefaults, byte? localCount = null)
    {
        var story = StoryFixtureBuilder.Create(version);
        story[routineAddress] = localCount ?? checked((byte)localDefaults.Length);
        if (version <= 4)
        {
            for (var index = 0; index < localDefaults.Length; index++)
            {
                StoryFixtureBuilder.WriteWord(story, routineAddress + 1 + index * sizeof(ushort), localDefaults[index]);
            }
        }

        RecalculateChecksum(story);
        return StoryFile.Parse(story);
    }

    private static void RecalculateChecksum(Span<byte> story)
    {
        if (story[0] >= 3)
        {
            StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        }
    }
}
