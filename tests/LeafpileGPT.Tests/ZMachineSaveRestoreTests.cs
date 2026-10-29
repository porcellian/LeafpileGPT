using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class ZMachineSaveRestoreTests
{
    private const uint CodeAddress = 850;

    [Fact]
    public void SaveRestoreRoundTripRestoresMemoryFramesPcAndPreservesFlags2()
    {
        var story = CreateStory(5);
        var store = new MemorySaveStore();
        var state = ZMachineExecutionState.Start(story, saveStore: store);
        state.CallRoutine(100, [0xAAAA], 880, 18);
        state.WriteVariable(16, 0x1234);
        state.PushEvaluationStack(0xCAFE);
        state.SetProgramCounter(875);

        Assert.True(state.SaveGame());
        state.WriteVariable(16, 0x4321);
        state.PopEvaluationStack();
        state.Memory.WriteWord(0x10, 0x0003);
        state.SetProgramCounter(900);

        Assert.True(state.RestoreGame());

        Assert.Equal((ushort)0x1234, state.ReadVariable(16));
        Assert.Equal((ushort)0xCAFE, Assert.Single(state.EvaluationStack));
        Assert.Equal((uint)875, state.ProgramCounter);
        Assert.Equal((ushort)0x0003, state.Memory.ReadWord(0x10));
        Assert.Equal(2, state.CallStack.Length);
        Assert.Equal((ushort)0xAAAA, Assert.Single(state.CallStack[1].Locals));
        Assert.Equal((uint)880, state.CallStack[1].ReturnProgramCounter);
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
    public void SnapshotMappingRoundTripsEverySupportedVersion(byte version)
    {
        var story = CreateStory(version);
        var state = ZMachineExecutionState.Start(story);
        state.SetProgramCounter(875);
        state.PushEvaluationStack(0xCAFE);
        var save = state.CaptureSaveState();
        var decoded = QuetzalCodec.Decode(QuetzalCodec.Encode(save, story), story);
        state.PopEvaluationStack();
        state.SetProgramCounter(900);

        state.ApplySaveState(decoded);

        Assert.Equal((uint)875, state.ProgramCounter);
        Assert.Equal((ushort)0xCAFE, Assert.Single(state.EvaluationStack));
        Assert.Equal(version == 6, state.CallStack[0].Locals.Length > 0);
    }

    [Fact]
    public void RestoreMissingSaveReturnsFailureWithoutChangingMachine()
    {
        var state = ZMachineExecutionState.Start(CreateStory(5), saveStore: new MemorySaveStore());
        state.WriteVariable(16, 99);
        state.SetProgramCounter(860);

        Assert.False(state.RestoreGame());
        Assert.Equal((ushort)99, state.ReadVariable(16));
        Assert.Equal((uint)860, state.ProgramCounter);
    }

    [Fact]
    public void RestoreRejectsDifferentStoryBeforeChangingLiveState()
    {
        var store = new MemorySaveStore();
        var otherStoryBytes = StoryFixtureBuilder.Create(5);
        StoryFixtureBuilder.WriteWord(otherStoryBytes, 0x02, 43);
        StoryFixtureBuilder.WriteWord(otherStoryBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(otherStoryBytes));
        var otherStory = StoryFile.Parse(otherStoryBytes);
        store.SetImage(QuetzalCodec.Encode(
            new QuetzalSaveData(5, QuetzalStoryIdentity.FromStory(otherStory, 875),
                otherStory.Bytes[..otherStory.Header.StaticMemoryBase].ToArray().ToImmutableArray(),
                [new QuetzalStackFrame(0, false, 0, 0, [], [], IsDummy: true)], []), otherStory));
        var state = ZMachineExecutionState.Start(CreateStory(5), saveStore: store);
        state.WriteVariable(16, 99);
        state.SetProgramCounter(860);

        Assert.Throws<QuetzalStoryMismatchException>(() => state.RestoreGame());
        Assert.Equal((ushort)99, state.ReadVariable(16));
        Assert.Equal((uint)860, state.ProgramCounter);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void SaveAndRestoreOpcodesStoreOneAndTwoAtSavedContinuation(byte version)
    {
        var store = new MemorySaveStore();
        var state = ZMachineExecutionState.Start(CreateStory(version, storeByte: 16), saveStore: store);
        var executor = new ZMachineOpcodeExecutor();
        executor.Execute(Instruction("save", store: 16, nextPc: 852), state);
        Assert.Equal((ushort)1, state.ReadVariable(16));
        state.WriteVariable(16, 77);

        executor.Execute(Instruction("restore", store: 17, nextPc: 862), state);

        Assert.Equal((uint)852, state.ProgramCounter);
        Assert.Equal((ushort)2, state.ReadVariable(16));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreOpcodeResumesAtInputForHostCheckpoints(bool markedCheckpoint)
    {
        const uint inputPc = 850;
        var storyBytes = StoryFixtureBuilder.Create(4);
        storyBytes[inputPc] = 0xE4;
        storyBytes[inputPc + 1] = 0xAF;
        storyBytes[inputPc + 2] = 149;
        storyBytes[inputPc + 3] = 92;
        storyBytes[400] = 1;
        storyBytes[500] = 4;
        StoryFixtureBuilder.WriteWord(storyBytes, 64 + (149 - 16) * 2, 400);
        StoryFixtureBuilder.WriteWord(storyBytes, 64 + (92 - 16) * 2, 500);
        StoryFixtureBuilder.WriteWord(storyBytes, 64 + (228 - 16) * 2, 0xBEEF);
        StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        var story = StoryFile.Parse(storyBytes);
        var store = new MemorySaveStore();
        var state = ZMachineExecutionState.Start(story, saveStore: store);
        var savedState = state.CaptureSaveState(inputPc);
        if (markedCheckpoint)
        {
            savedState = QuetzalCodec.MarkAsHostCheckpoint(savedState);
        }

        store.SetImage(QuetzalCodec.Encode(savedState, story));
        state.WriteVariable(92, 0x8BD4);
        state.WriteVariable(228, 0x1234);

        new ZMachineOpcodeExecutor().Execute(Instruction("restore", store: 17, nextPc: 862), state);

        Assert.True(state.LastRestoreWasHostCheckpoint);
        Assert.Equal(inputPc, state.ProgramCounter);
        Assert.Equal((ushort)500, state.ReadVariable(92));
        Assert.Equal((ushort)0xBEEF, state.ReadVariable(228));
        state.Memory.ValidateWritableRange(state.ReadVariable(149), 3);
        state.Memory.ValidateWritableRange(state.ReadVariable(92), 18);
    }

    [Fact]
    public void RestorePreservesV4ScreenLayoutAndCursorState()
    {
        var state = ZMachineExecutionState.Start(CreateStory(4));
        var screen = state.OutputStreams.Screen;
        var screenOperations = new List<ZMachineScreenOperation>();
        screen.OperationWriter = screenOperations.Add;
        screen.SetDimensions(80, 25);
        screen.SplitWindow(2);
        screen.SetWindow(1);
        screen.SetCursor(2, 67);
        var save = state.CaptureSaveState(850);
        screenOperations.Clear();

        state.ApplySaveState(save);

        Assert.Same(screen, state.OutputStreams.Screen);
        Assert.Equal(2, screen.UpperWindowHeight);
        Assert.Equal((short)1, screen.SelectedWindow);
        Assert.Equal(new ZMachineCursorPosition(2, 67), screen.CurrentCursor);
        Assert.Contains(screenOperations, operation =>
            operation.Kind == ZMachineScreenOperationKind.ClearWindow && operation.Window == 0);
        Assert.DoesNotContain(screenOperations, operation => operation.Kind == ZMachineScreenOperationKind.ClearScreen);
    }

    [Fact]
    public void RestoreCollapsesV3UpperWindowAsRequiredByTheStandard()
    {
        var state = ZMachineExecutionState.Start(CreateStory(3));
        var screen = state.OutputStreams.Screen;
        screen.SetDimensions(80, 25);
        screen.SplitWindow(2);
        screen.SetWindow(1);
        var save = state.CaptureSaveState(850);

        state.ApplySaveState(save);

        Assert.Equal(0, screen.UpperWindowHeight);
        Assert.Equal((short)0, screen.SelectedWindow);
        Assert.Equal(new ZMachineCursorPosition(25, 1), screen.GetCursor(0));
    }

    [Fact]
    public void ExtendedSavePassesSaveAreaFilenameAndPromptToHostStore()
    {
        var store = new MemorySaveStore();
        var state = ZMachineExecutionState.Start(CreateStory(5), saveStore: store);
        state.Memory.WriteByte(100, 3);
        state.Memory.WriteByte(101, (byte)'s');
        state.Memory.WriteByte(102, (byte)'a');
        state.Memory.WriteByte(103, (byte)'v');
        var instruction = new DecodedInstruction(CodeAddress, 856, InstructionForm.Extended,
            OpcodeClass.Extended, 0, new OpcodeDefinition(OpcodeClass.Extended, 0, "save", 5, 8, 3, 4,
                StoresResult: true),
            [new(OperandEncoding.SmallConstant, 120), new(OperandEncoding.SmallConstant, 8),
                new(OperandEncoding.SmallConstant, 100), new(OperandEncoding.SmallConstant, 0)],
            16, null, []);

        new ZMachineOpcodeExecutor().Execute(instruction, state);

        Assert.Equal((ushort)120, store.SavedOptions?.TableAddress);
        Assert.Equal((ushort)8, store.SavedOptions?.ByteCount);
        Assert.Equal("sav", store.SavedOptions?.SuggestedName);
        Assert.Equal(false, store.SavedOptions?.Prompt);
    }

    [Fact]
    public void FailedVersionThreeRestoreDoesNotTakeTrueBranch()
    {
        var state = ZMachineExecutionState.Start(CreateStory(3));
        var branch = new DecodedBranch(true, 4, 900, null);
        new ZMachineOpcodeExecutor().Execute(Instruction("restore", branch: branch, nextPc: 855), state);

        Assert.Equal((uint)855, state.ProgramCounter);
    }

    [Fact]
    public void SuccessfulVersionThreeRestoreResumesAtSavedBranchDescriptor()
    {
        var storyBytes = StoryFixtureBuilder.Create(3);
        storyBytes[851] = 0xC4;
        StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        var store = new MemorySaveStore();
        var state = ZMachineExecutionState.Start(StoryFile.Parse(storyBytes), saveStore: store);
        var executor = new ZMachineOpcodeExecutor();
        var saveBranch = new DecodedBranch(true, 4, 854, null, DescriptorAddress: 851, EncodedLength: 1);
        executor.Execute(Instruction("save", branch: saveBranch, nextPc: 852), state);

        executor.Execute(Instruction("restore", branch: saveBranch, nextPc: 862), state);

        Assert.Equal((uint)854, state.ProgramCounter);
    }

    [Fact]
    public void UndoRestoresOneSnapshotAndRestartReturnsToOriginalStory()
    {
        var state = ZMachineExecutionState.Start(CreateStory(5, storeByte: 17));
        var executor = new ZMachineOpcodeExecutor();
        var initialGlobal = state.ReadVariable(16);
        state.WriteVariable(16, 12);
        executor.Execute(Instruction("save_undo", store: 17, nextPc: 852), state);
        state.WriteVariable(16, 44);
        executor.Execute(Instruction("restore_undo", store: 18, nextPc: 862), state);

        Assert.Equal((ushort)12, state.ReadVariable(16));
        Assert.Equal((ushort)2, state.ReadVariable(17));
        Assert.Equal((uint)852, state.ProgramCounter);
        Assert.False(state.RestoreUndo());

        state.WriteVariable(16, 55);
        state.Memory.WriteWord(0x10, 0x0003);
        executor.Execute(Instruction("restart"), state);
        Assert.Equal((uint)CodeAddress, state.ProgramCounter);
        Assert.Equal(initialGlobal, state.ReadVariable(16));
        Assert.Equal((ushort)0x0003, state.Memory.ReadWord(0x10));
    }

    [Fact]
    public void RestartUsesOriginalV6MainRoutineAndCanRunAfterHalt()
    {
        var bytes = StoryFixtureBuilder.Create(6);
        bytes[800] = 0;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        var state = ZMachineExecutionState.Start(StoryFile.Parse(bytes));
        state.Halt();

        state.Restart();

        Assert.False(state.IsHalted);
        Assert.Equal((uint)801, state.ProgramCounter);
        Assert.Single(state.CallStack);
    }

    private static StoryFile CreateStory(byte version, byte? storeByte = null)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        bytes[400] = 1;
        if (storeByte is { } resultVariable)
        {
            bytes[851] = resultVariable;
        }

        if (version == 6)
        {
            bytes[800] = 1;
        }

        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return StoryFile.Parse(bytes);
    }

    private static DecodedInstruction Instruction(
        string mnemonic,
        byte? store = null,
        DecodedBranch? branch = null,
        uint? nextPc = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.ZeroOperand, 0, mnemonic, 1, 8,
            0, 0, store is not null, branch is not null);
        return new DecodedInstruction(CodeAddress, nextPc ?? CodeAddress + 1, InstructionForm.Short,
            OpcodeClass.ZeroOperand, 0, definition, ImmutableArray<DecodedOperand>.Empty, store, branch,
            ImmutableArray<byte>.Empty);
    }

    private sealed class MemorySaveStore : IZMachineSaveStore
    {
        private byte[]? _savedImage;
        public ZMachineSaveOptions? SavedOptions { get; private set; }

        public void SetImage(byte[] image) => _savedImage = image;

        public bool Save(ReadOnlyMemory<byte> ifzsImage, ZMachineSaveOptions options)
        {
            _savedImage = ifzsImage.ToArray();
            SavedOptions = options;
            return true;
        }

        public byte[]? Restore(ZMachineSaveOptions options) => _savedImage?.ToArray();
    }
}
