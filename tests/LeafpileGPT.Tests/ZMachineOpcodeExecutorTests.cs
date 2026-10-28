namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineOpcodeExecutorTests
{
    private const uint CodeAddress = 850;

    [Fact]
    public void ArithmeticWrapsToSixteenBitsAndStoresResult()
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction("add", [Constant(0x7FFF), Constant(1)], store: 16), state);
        Assert.Equal((ushort)0x8000, state.Memory.ReadWord(64));
    }

    [Theory]
    [InlineData("div", -7, 3, -2)]
    [InlineData("mod", -7, 3, -1)]
    [InlineData("div", 7, -3, -2)]
    [InlineData("mod", 7, -3, 1)]
    public void DivisionAndRemainderUseSignedSixteenBitOperands(string opcode, short left, short right, short expected)
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction(opcode, [Constant(unchecked((ushort)left)), Constant(unchecked((ushort)right))], store: 16), state);
        Assert.Equal(unchecked((ushort)expected), state.Memory.ReadWord(64));
    }

    [Theory]
    [InlineData("div")]
    [InlineData("mod")]
    public void DivisionByZeroFails(string opcode)
    {
        var (state, executor) = CreateMachine();
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction(opcode, [Constant(7), Constant(0)], store: 16), state));
        Assert.True(state.IsHalted);
    }

    [Theory]
    [InlineData("art_shift", 0x8001, -1, 0xC000)]
    [InlineData("log_shift", 0x8001, -1, 0x4000)]
    [InlineData("log_shift", 0x4001, 2, 0x0004)]
    public void ShiftOpcodesUseSignedCountsAndDistinctRightShiftRules(string opcode, ushort value, short count, ushort expected)
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction(opcode, [Constant(value), Constant(unchecked((ushort)count))], store: 16), state);
        Assert.Equal(expected, state.Memory.ReadWord(64));
    }

    [Fact]
    public void ShiftRejectsCountsOutsideStandardRange()
    {
        var (state, executor) = CreateMachine();
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(
            Instruction("art_shift", [Constant(1), Constant(16)], store: 16), state));
    }

    [Fact]
    public void PredictableRandomSeedRepeatsAndZeroRestoresRandomMode()
    {
        var (state, _) = CreateMachine();
        Assert.Equal((ushort)0, state.GetRandomNumber(-3));
        Assert.Equal(new ushort[] { 1, 2, 3, 1, 2 }, Enumerable.Range(0, 5).Select(_ => state.GetRandomNumber(10)));
        Assert.Equal((ushort)0, state.GetRandomNumber(0));
        Assert.False(state.IsPredictableRandomMode);
        Assert.InRange(state.GetRandomNumber(7), (ushort)1, (ushort)7);
    }

    [Fact]
    public void LargePredictableRandomSeedProducesRepeatableInRangeValues()
    {
        var (first, _) = CreateMachine();
        var (second, _) = CreateMachine();
        first.GetRandomNumber(-1234);
        second.GetRandomNumber(-1234);
        var firstSequence = Enumerable.Range(0, 12).Select(_ => first.GetRandomNumber(100)).ToArray();
        var secondSequence = Enumerable.Range(0, 12).Select(_ => second.GetRandomNumber(100)).ToArray();
        Assert.Equal(firstSequence, secondSequence);
        Assert.All(firstSequence, value => Assert.InRange(value, (ushort)1, (ushort)100));
    }

    [Fact]
    public void JeAcceptsFourOperandsAndBranchesWhenAnyMatches()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        bytes[CodeAddress] = 0xC1;
        bytes[CodeAddress + 1] = 0x00;
        bytes[CodeAddress + 2] = 0;
        bytes[CodeAddress + 3] = 3;
        bytes[CodeAddress + 4] = 0;
        bytes[CodeAddress + 5] = 2;
        bytes[CodeAddress + 6] = 0;
        bytes[CodeAddress + 7] = 3;
        bytes[CodeAddress + 8] = 0;
        bytes[CodeAddress + 9] = 3;
        bytes[CodeAddress + 10] = 0xC3;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        var state = ZMachineExecutionState.Start(StoryFile.Parse(bytes));
        var instruction = new ZMachineInstructionDecoder(state.Memory).Decode(CodeAddress);

        new ZMachineOpcodeExecutor().Execute(instruction, state);

        Assert.Equal(4, instruction.Operands.Length);
        Assert.Equal("je", instruction.Definition.Mnemonic);
        Assert.Equal(instruction.NextProgramCounter + 1, state.ProgramCounter);
    }

    [Fact]
    public void VariableReferencesUseTheEncodedVariableNumber()
    {
        var (state, executor) = CreateMachine();
        state.Memory.WriteWord(64, 20);
        executor.Execute(Instruction("inc", [new DecodedOperand(OperandEncoding.Variable, 16)]), state);
        Assert.Equal((ushort)21, state.Memory.ReadWord(64));
    }

    [Fact]
    public void StoreAndLoadUseVariableReferencesWithoutPoppingStackVariableZero()
    {
        var (state, executor) = CreateMachine();
        state.PushEvaluationStack(5);
        executor.Execute(Instruction("store", [Constant(0), Constant(9)]), state);
        executor.Execute(Instruction("load", [Constant(0)], store: 16), state);
        Assert.Equal((ushort)9, state.EvaluationStack[^1]);
        Assert.Equal((ushort)9, state.Memory.ReadWord(64));
    }

    [Fact]
    public void ArrayLoadsAndStoresUseByteAndWordOffsets()
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction("storeb", [Constant(500), Constant(4), Constant(0x1AB)]), state);
        executor.Execute(Instruction("storew", [Constant(500), Constant(2), Constant(0xABCD)]), state);
        executor.Execute(Instruction("loadb", [Constant(500), Constant(4)], store: 16), state);
        Assert.Equal((ushort)0xAB, state.Memory.ReadWord(64));
        executor.Execute(Instruction("loadw", [Constant(500), Constant(2)], store: 17), state);
        Assert.Equal((ushort)0xABCD, state.Memory.ReadWord(66));
    }

    [Fact]
    public void ArrayWritesRespectDynamicMemoryProtection()
    {
        var (state, executor) = CreateMachine();
        Assert.Throws<ZMachineMemoryAccessException>(() => executor.Execute(
            Instruction("storeb", [Constant(700), Constant(0), Constant(1)]), state));
    }

    [Fact]
    public void CopyTableUsesOverlapSafePositiveCopyAndForwardNegativeCopy()
    {
        var (state, executor) = CreateMachine();
        for (ushort i = 0; i < 5; i++)
        {
            state.Memory.WriteByte((ushort)(500 + i), (byte)(i + 1));
        }
        executor.Execute(Instruction("copy_table", [Constant(500), Constant(501), Constant(4)]), state);
        Assert.Equal(new byte[] { 1, 1, 2, 3, 4 }, Enumerable.Range(0, 5).Select(i => state.Memory.ReadByte((ushort)(500 + i))));
        executor.Execute(Instruction("copy_table", [Constant(500), Constant(501), Constant(0xFFFC)]), state);
        Assert.Equal(new byte[] { 1, 1, 1, 1, 1 }, Enumerable.Range(0, 5).Select(i => state.Memory.ReadByte((ushort)(500 + i))));
    }

    [Fact]
    public void CopyTableWithZeroDestinationClearsTheSource()
    {
        var (state, executor) = CreateMachine();
        state.Memory.WriteByte(500, 12);
        state.Memory.WriteByte(501, 34);
        executor.Execute(Instruction("copy_table", [Constant(500), Constant(0), Constant(2)]), state);
        Assert.Equal(new byte[] { 0, 0 }, new[] { state.Memory.ReadByte(500), state.Memory.ReadByte(501) });
    }

    [Fact]
    public void ScanTableFindsByteAndWordEntriesAndBranches()
    {
        var (state, executor) = CreateMachine();
        state.Memory.WriteByte(500, 4);
        state.Memory.WriteByte(501, 9);
        executor.Execute(Instruction("scan_table", [Constant(9), Constant(500), Constant(2), Constant(1)], store: 16,
            branch: new DecodedBranch(true, 2, CodeAddress, null)), state);
        Assert.Equal((ushort)501, state.Memory.ReadWord(64));
        Assert.Equal(CodeAddress, state.ProgramCounter);
        state.Memory.WriteWord(510, 0x1234);
        executor.Execute(Instruction("scan_table", [Constant(0x1234), Constant(510), Constant(1)], store: 17,
            branch: new DecodedBranch(false, 3, CodeAddress + 2, null)), state);
        Assert.Equal((ushort)510, state.Memory.ReadWord(66));
    }

    [Fact]
    public void TakenComparisonBranchCanReturnFromRoutine()
    {
        var (state, executor) = CreateMachine();
        state.CallRoutine(100, [], CodeAddress, storeVariable: 16);
        executor.Execute(Instruction("jz", [Constant(0)], branch: new DecodedBranch(true, 1, null, true)), state);
        Assert.Equal((ushort)1, state.Memory.ReadWord(64));
        Assert.Equal(CodeAddress, state.ProgramCounter);
    }

    [Fact]
    public void PushStackBranchesOnCapacityAndWritesVersionSixUserStack()
    {
        var state = CreateVersionSixMachine();
        var executor = new ZMachineOpcodeExecutor();
        state.Memory.WriteWord(500, 1);

        executor.Execute(Instruction("push_stack", [Constant(0x4567), Constant(500)],
            branch: new DecodedBranch(true, 3, CodeAddress + 2, null)), state);
        Assert.Equal((ushort)0x4567, state.Memory.ReadWord(502));
        Assert.Equal((ushort)0, state.Memory.ReadWord(500));
        Assert.Equal(CodeAddress + 2, state.ProgramCounter);

        executor.Execute(Instruction("push_stack", [Constant(0x1234), Constant(500)],
            branch: new DecodedBranch(true, 3, CodeAddress + 2, null)), state);
        Assert.Equal((ushort)0x4567, state.Memory.ReadWord(502));
        Assert.Equal((ushort)0, state.Memory.ReadWord(500));
        Assert.Equal(CodeAddress + 1, state.ProgramCounter);
    }

    [Fact]
    public void JumpUsesSignedRelativeOffsetFromFollowingInstruction()
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction("jump", [Constant(unchecked((ushort)-3))], nextPc: CodeAddress + 3), state);
        Assert.Equal(CodeAddress - 2, state.ProgramCounter);
    }

    [Fact]
    public void QuitHaltsAndSubsequentExecutionFails()
    {
        var (state, executor) = CreateMachine();
        executor.Execute(Instruction("quit", []), state);
        Assert.True(state.IsHalted);
        Assert.Throws<ZMachineExecutionException>(() => executor.Execute(Instruction("nop", []), state));
    }

    [Fact]
    public void UnsupportedOpcodeFailsFastWithContext()
    {
        var (state, executor) = CreateMachine();
        var exception = Assert.Throws<ZMachineOpcodeNotImplementedException>(() => executor.Execute(Instruction("print", []), state));
        Assert.Equal("print", exception.Mnemonic);
        Assert.Equal(CodeAddress, exception.ProgramCounter);
        Assert.Equal(CodeAddress, state.ProgramCounter);
    }

    private static (ZMachineExecutionState State, ZMachineOpcodeExecutor Executor) CreateMachine()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        bytes[400] = 0;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        var state = ZMachineExecutionState.Start(StoryFile.Parse(bytes));
        return (state, new ZMachineOpcodeExecutor());
    }

    private static ZMachineExecutionState CreateVersionSixMachine()
    {
        var bytes = StoryFixtureBuilder.Create(6);
        bytes[800] = 0;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);

    private static DecodedInstruction Instruction(
        string mnemonic,
        DecodedOperand[] operands,
        byte? store = null,
        DecodedBranch? branch = null,
        uint? nextPc = null)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Extended, 0, mnemonic, 1, 8,
            checked((byte)operands.Length), checked((byte)operands.Length), store is not null, branch is not null);
        return new DecodedInstruction(CodeAddress, nextPc ?? CodeAddress + 1, InstructionForm.Extended,
            OpcodeClass.Extended, 0, definition, operands.ToImmutableArray(), store, branch, ImmutableArray<byte>.Empty);
    }
}
