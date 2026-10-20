using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineSystemOpcodeTests
{
    [Theory]
    [InlineData(0x14, 0x7fff, 1, 0x8000)]
    [InlineData(0x15, 0, 1, 0xffff)]
    [InlineData(0x16, 0xfffe, 3, 0xfffa)]
    [InlineData(0x17, 0x8000, 0xffff, 0x8000)]
    [InlineData(0x18, 0xfffb, 3, 0xfffe)]
    public void ArithmeticOpcodesUseSixteenBitSignedRules(byte opcode, ushort left, ushort right, ushort expected)
    {
        var state = CreateState(5, (byte)(0xc0 | opcode), 0x0f, (byte)(left >> 8), (byte)left,
            (byte)(right >> 8), (byte)right, 16);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(expected, state.ReadVariableValue(16));
    }

    [Fact]
    public void DivisionAndRemainderByZeroRaiseZMachineErrors()
    {
        var division = CreateState(5, 0x17, 1, 0, 0, 0, 16);
        var remainder = CreateState(5, 0x18, 1, 0, 0, 0, 16);

        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(division).ExecuteNext());
        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(remainder).ExecuteNext());
    }

    [Fact]
    public void BitwiseOpcodesAndVersionedNotStoreSixteenBitResults()
    {
        var or = CreateState(5, 0x08, 0x0f, 0xf0, 0x10);
        var and = CreateState(5, 0x09, 0x0f, 0xf0, 0x10);
        var not = CreateState(5, 0xf8, 0x7f, 0x0f, 0x10);

        new ZMachineOpcodeExecutor(or).ExecuteNext();
        new ZMachineOpcodeExecutor(and).ExecuteNext();
        new ZMachineOpcodeExecutor(not).ExecuteNext();

        Assert.Equal(0x00ff, or.ReadVariableValue(16));
        Assert.Equal(0, and.ReadVariableValue(16));
        Assert.Equal(0xfff0, not.ReadVariableValue(16));
    }

    [Fact]
    public void LoadAndStoreTreatStackVariableAsAnInPlaceIndirectReference()
    {
        var state = CreateState(5, 0x4d, 0, 9, 0xae, 0, 0x10);
        state.CallStack.Push(4);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal(9, state.CallStack.Peek());
        Assert.Equal(9, state.ReadVariableValue(16));
        Assert.Equal(1, state.CallStack.EvaluationStackDepth);
    }

    [Fact]
    public void IncrementAndDecrementUpdateAnIndirectStackVariableInPlace()
    {
        var state = CreateState(5, 0xa5, 0, 0xa6, 0);
        state.CallStack.Push(0xffff);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        Assert.Equal(0, state.CallStack.Peek());
        executor.ExecuteNext();

        Assert.Equal(0xffff, state.CallStack.Peek());
        Assert.Equal(1, state.CallStack.EvaluationStackDepth);
    }

    [Fact]
    public void IndirectVariableOperandsUseTheirEncodedVariableNumber()
    {
        var state = CreateState(5, 0x44, 16, 5, 0xc3);
        state.WriteVariable(16, 5);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(4, state.ReadVariableValue(16));
        Assert.Equal(0x205, state.ProgramCounter);
    }

    [Fact]
    public void PullThroughStackVariableLeavesItsTopValueAndDepthInPlace()
    {
        var state = CreateState(5, 0xe9, 0xbf, 0);
        state.CallStack.Push(1);
        state.CallStack.Push(2);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(2, state.CallStack.Peek());
        Assert.Equal(2, state.CallStack.EvaluationStackDepth);
    }

    [Fact]
    public void PullStoresThePoppedEvaluationValueIntoItsIndirectDestination()
    {
        var state = CreateState(5, 0xe9, 0x7f, 16);
        state.CallStack.Push(0x1234);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(0x1234, state.ReadVariableValue(16));
        Assert.Equal(0, state.CallStack.EvaluationStackDepth);
    }

    [Fact]
    public void LoadWordAndByteReadFromDynamicAndStaticMemory()
    {
        var story = MachineStoryFactory.Create(5);
        story[0x82] = 0x12;
        story[0x83] = 0x34;
        story[0x700] = 0xa5;
        story[0x200] = 0xcf;
        story[0x201] = 0x5f;
        story[0x202] = 0x80;
        story[0x203] = 1;
        story[0x204] = 0x10;
        story[0x205] = 0xd0;
        story[0x206] = 0x1f;
        story[0x207] = 0x07;
        story[0x208] = 0x00;
        story[0x209] = 0x00;
        story[0x20a] = 0x10;
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        Assert.Equal(0x1234, state.ReadVariableValue(16));
        executor.ExecuteNext();
        Assert.Equal(0x00a5, state.ReadVariableValue(16));
    }

    [Fact]
    public void StoreWordAndByteWriteDynamicMemoryAndRejectStaticMemory()
    {
        var state = CreateState(5, 0xe1, 0x13, 0, 0x20, 1, 0xab, 0xcd,
            0xe2, 0x17, 0, 0x20, 4, 0xff);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal(0xabcd, state.Memory.ReadWord(0x22));
        Assert.Equal(0xff, state.Memory.ReadByte(0x24));

        var invalid = CreateState(5, 0xe2, 0x17, 0x03, 0x00, 0, 0, 0x7f);
        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(invalid).ExecuteNext());
    }

    [Fact]
    public void RandomOpcodeSupportsPredictableSeededSequencesAndReset()
    {
        var state = CreateState(5, 0xe7, 0x3f, 0xff, 0xfb, 0x10,
            0xe7, 0x7f, 5, 0x10,
            0xe7, 0x7f, 5, 0x11,
            0xe7, 0x7f, 0, 0x12);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        Assert.Equal(0, state.ReadVariableValue(16));
        executor.ExecuteNext();
        Assert.Equal(1, state.ReadVariableValue(16));
        executor.ExecuteNext();
        Assert.Equal(2, state.ReadVariableValue(17));
        Assert.True(state.Random.IsPredictable);

        executor.ExecuteNext();

        Assert.False(state.Random.IsPredictable);
        Assert.Equal(0, state.ReadVariableValue(18));
    }

    [Fact]
    public void CopyTableHandlesOverlapDirectionAndZeroFill()
    {
        var state = CreateState(5,
            0xfd, 0x07, 0, 0x20, 0, 0x21, 3,
            0xfd, 0x07, 0, 0x30, 0, 0, 3);
        state.Memory.WriteByte(0x20, 1);
        state.Memory.WriteByte(0x21, 2);
        state.Memory.WriteByte(0x22, 3);
        state.Memory.WriteByte(0x23, 4);
        state.Memory.WriteByte(0x30, 0xaa);
        state.Memory.WriteByte(0x31, 0xbb);
        state.Memory.WriteByte(0x32, 0xcc);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();
        executor.ExecuteNext();

        Assert.Equal(new byte[] { 1, 1, 2, 3 }, Enumerable.Range(0, 4).Select(index => state.Memory.ReadByte((ushort)(0x20 + index))).ToArray());
        Assert.Equal(new byte[] { 0, 0, 0 }, Enumerable.Range(0, 3).Select(index => state.Memory.ReadByte((ushort)(0x30 + index))).ToArray());
    }

    [Fact]
    public void NegativeCopyTableSizeCopiesForwardsAndCanOverwriteItsSource()
    {
        var state = CreateState(5, 0xfd, 0x03, 0, 0x20, 0, 0x21, 0xff, 0xfd);
        state.Memory.WriteByte(0x20, 1);
        state.Memory.WriteByte(0x21, 2);
        state.Memory.WriteByte(0x22, 3);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(new byte[] { 1, 1, 1, 1 }, Enumerable.Range(0, 4).Select(index => state.Memory.ReadByte((ushort)(0x20 + index))).ToArray());
    }

    [Theory]
    [InlineData(2, 1, 8)]
    [InlineData(2, 0xffff, 0xfffe)]
    [InlineData(3, 0x8001, 0xfffe)]
    public void ExtendedShiftOpcodesApplyLogicalAndArithmeticShiftRules(byte opcode, ushort number, ushort places)
    {
        var state = CreateState(5, 0xbe, opcode, 0x0f, (byte)(number >> 8), (byte)number,
            (byte)(places >> 8), (byte)places, 0x10);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        var expected = (opcode, number, places) switch
        {
            (2, 1, 8) => (ushort)0x0100,
            (2, 0xffff, 0xfffe) => (ushort)0x3fff,
            (3, 0x8001, 0xfffe) => (ushort)0xe000,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, state.ReadVariableValue(16));
    }

    [Fact]
    public void ExtendedShiftsRejectCountsOutsideTheDefinedRange()
    {
        var state = CreateState(5, 0xbe, 2, 0x0f, 0, 1, 0, 16, 0x10);

        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineOpcodeExecutor(state).ExecuteNext());
    }

    [Fact]
    public void VersionSixPopStackCanDiscardEvaluationStackValues()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x201] = 0xbe;
        story[0x202] = 21;
        story[0x203] = 0x7f;
        story[0x204] = 2;
        var state = new ZMachineState(StoryLoader.Load(story));
        state.SetProgramCounter(0x201);
        state.CallStack.Push(1);
        state.CallStack.Push(2);

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(0, state.CallStack.EvaluationStackDepth);
    }

    [Fact]
    public void VersionSixPopStackCanDiscardValuesFromAnExplicitUserStack()
    {
        var story = MachineStoryFactory.Create(6);
        story[0x201] = 0xbe;
        story[0x202] = 21;
        story[0x203] = 0x5f;
        story[0x204] = 1;
        story[0x205] = 0x30;
        var state = new ZMachineState(StoryLoader.Load(story));
        state.SetProgramCounter(0x201);
        state.Memory.WriteWord(0x30, 2);
        Assert.True(state.CallStack.PushUserStack(0x1111, 0x30));
        Assert.True(state.CallStack.PushUserStack(0x2222, 0x30));

        new ZMachineOpcodeExecutor(state).ExecuteNext();

        Assert.Equal(1, state.Memory.ReadWord(0x30));
    }

    private static ZMachineState CreateState(byte version, params byte[] instruction)
    {
        var story = MachineStoryFactory.Create(version);
        instruction.CopyTo(story, version == 6 ? 0x201 : 0x200);
        return new ZMachineState(StoryLoader.Load(story));
    }
}
