using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineOpcodeExecutorTests
{
    [Fact]
    public void EvaluatesConditionalBranchAndUsesRelativeOffset()
    {
        var state = CreateState(5, 0x01, 1, 1, 0xc3);

        var result = new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.False(result.Halted);
        Assert.Equal(0x205, state.ProgramCounter);
    }

    [Fact]
    public void DecrementsReferencedGlobalAndBranchesOnSignedComparison()
    {
        var state = CreateState(5, 0x04, 16, 5, 0xc3);
        state.WriteVariable(16, 5);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(4, state.ReadVariableValue(16));
        Assert.Equal(0x205, state.ProgramCounter);
    }

    [Fact]
    public void CallsRoutineAndStoresItsReturnValueInTheCaller()
    {
        var story = MachineStoryFactory.Create(5);
        story[0x200] = 0x88; // call_1s with a large packed routine address
        story[0x201] = 0x01;
        story[0x202] = 0x40; // 0x140 * 4 = 0x500
        story[0x203] = 0x10;
        story[0x500] = 0; // zero locals
        story[0x501] = 0xb0; // rtrue
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        Assert.Equal(0x501, state.ProgramCounter);

        executor.ExecuteNext();

        Assert.Equal(0x204, state.ProgramCounter);
        Assert.Equal(1, state.ReadVariableValue(16));
        Assert.Equal(1, state.CallStack.FrameCount);
    }

    [Fact]
    public void CallsAddressZeroAndStoresFalseImmediately()
    {
        var state = CreateState(5, 0x88, 0, 0, 0x10);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(0x204, state.ProgramCounter);
        Assert.Equal(0, state.ReadVariableValue(16));
        Assert.Equal(1, state.CallStack.FrameCount);
    }

    [Fact]
    public void ThrowReturnsFromCaughtFrameAndStoresValueInCaller()
    {
        var story = MachineStoryFactory.Create(5);
        story[0x200] = 0x88;
        story[0x201] = 0x01;
        story[0x202] = 0x40;
        story[0x203] = 0x10;
        story[0x500] = 0;
        story[0x501] = 0xb9; // catch -> g1
        story[0x502] = 0x11;
        story[0x503] = 0xdc; // throw 42 g1
        story[0x504] = 0x6f;
        story[0x505] = 42;
        story[0x506] = 0x11;
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();
        var caughtFrame = state.ReadVariableValue(17);
        Assert.NotEqual((ushort)0, caughtFrame);
        executor.ExecuteNext();

        Assert.Equal(0x204, state.ProgramCounter);
        Assert.Equal(42, state.ReadVariableValue(16));
        Assert.Equal(1, state.CallStack.FrameCount);
    }

    [Fact]
    public void SaveOpcodeBranchesInVersionThreeAndStoresInVersionFour()
    {
        var v3 = CreateState(3, 0xb5, 0xc3);
        var v3Executor = new ZMachineOpcodeExecutor(v3, new FakeSaveRestoreService
        {
            SaveResult = new ZMachineSaveRestoreOutcome(true, 1)
        });
        v3Executor.ExecuteNext();

        var v4 = CreateState(4, 0xb5, 0x10);
        var v4Executor = new ZMachineOpcodeExecutor(v4, new FakeSaveRestoreService
        {
            SaveResult = new ZMachineSaveRestoreOutcome(false, 0)
        });
        v4Executor.ExecuteNext();

        Assert.Equal(0x203, v3.ProgramCounter);
        Assert.Equal(0, v4.ReadVariableValue(16));
        Assert.Equal(0x202, v4.ProgramCounter);
    }

    [Fact]
    public void VersionFiveUndoUsesExtendedOpcodeAndStoresFailureValue()
    {
        var state = CreateState(5, 0xbe, 9, 0xff, 0x10);
        var service = new FakeSaveRestoreService
        {
            SaveResult = new ZMachineSaveRestoreOutcome(false, ushort.MaxValue)
        };

        new ZMachineOpcodeExecutor(state, service).ExecuteNext();

        Assert.True(service.LastUndo);
        Assert.Equal(ushort.MaxValue, state.ReadVariableValue(16));
    }

    [Fact]
    public void SuccessfulRestoreReturnsControlToTheSaveServiceRestoredState()
    {
        var state = CreateState(5, 0xbe, 10, 0xff, 0x10);
        var service = new FakeSaveRestoreService
        {
            RestoreResult = new ZMachineSaveRestoreOutcome(true, 2, ResumedFromSavedState: true),
            RestoreProgramCounter = 0x300
        };

        var result = new ZMachineOpcodeExecutor(state, service).ExecuteNext();

        Assert.True(result.Restored);
        Assert.Equal(0x300, state.ProgramCounter);
    }

    [Fact]
    public void RestartPreservesOnlyTheTwoSpecifiedFlagsAndQuitHalts()
    {
        var story = MachineStoryFactory.Create(5);
        story[0x200] = 0xb7;
        story[0x201] = 0xba;
        story[0x10] = 0x12;
        story[0x11] = 0x34;
        var state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteWord(0x10, 0x8003);
        var executor = new ZMachineOpcodeExecutor(state);

        var restarted = executor.ExecuteNext();
        Assert.True(restarted.Restarted);
        Assert.Equal(0x200, state.ProgramCounter);
        Assert.Equal(0x1237, state.Memory.ReadWord(0x10));

        state.SetProgramCounter(0x201);
        var halted = executor.ExecuteNext();
        Assert.True(halted.Halted);
    }

    [Fact]
    public void SaveOpcodesFailClearlyWhenNoSaveServiceIsConfigured()
    {
        var state = CreateState(5, 0xbe, 9, 0xff, 0x10);

        var error = Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(state).ExecuteNext());

        Assert.Contains("save and restore service", error.Message);
    }

    [Fact]
    public void PublishesScreenUpdatesAndRuntimeErrorsToTheDisplayHost()
    {
        var state = CreateState(5, 0xb4);
        var display = new FakeDisplay();
        var result = new ZMachineOpcodeExecutor(state, display: display).ExecuteNext();

        Assert.False(result.Halted);
        Assert.Same(state.Screen, display.LastScreen);

        var saveState = CreateState(5, 0xbe, 9, 0xff, 0x10);
        var saveDisplay = new FakeDisplay();
        var error = Assert.Throws<ZMachineRuntimeException>(() =>
            new ZMachineOpcodeExecutor(saveState, display: saveDisplay).ExecuteNext());

        Assert.Equal(error.Message, saveDisplay.LastRuntimeError);
    }

    private static ZMachineState CreateState(byte version, params byte[] instruction)
    {
        var story = MachineStoryFactory.Create(version);
        var address = version == 6 ? 0x201 : 0x200;
        instruction.CopyTo(story, address);
        return new ZMachineState(StoryLoader.Load(story));
    }

    private sealed class FakeSaveRestoreService : IZMachineSaveRestoreService
    {
        public ZMachineSaveRestoreOutcome SaveResult { get; init; } = new(false, 0);

        public ZMachineSaveRestoreOutcome RestoreResult { get; init; } = new(false, 0);

        public int? RestoreProgramCounter { get; init; }

        public bool LastUndo { get; private set; }

        public ZMachineSaveRestoreOutcome Save(ZMachineState state, ZMachineInstruction instruction, bool undo)
        {
            LastUndo = undo;
            return SaveResult;
        }

        public ZMachineSaveRestoreOutcome Restore(ZMachineState state, ZMachineInstruction instruction, bool undo)
        {
            LastUndo = undo;
            if (RestoreProgramCounter.HasValue)
            {
                state.SetProgramCounter(RestoreProgramCounter.Value);
            }

            return RestoreResult;
        }
    }

    private sealed class FakeDisplay : IZMachineDisplay
    {
        public ZMachineScreen? LastScreen { get; private set; }

        public string? LastRuntimeError { get; private set; }

        public void Present(ZMachineScreen screen) => LastScreen = screen;

        public void ReportRuntimeError(string message) => LastRuntimeError = message;
    }
}
