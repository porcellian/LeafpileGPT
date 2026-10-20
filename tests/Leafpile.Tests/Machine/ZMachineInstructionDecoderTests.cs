using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineInstructionDecoderTests
{
    [Fact]
    public void DecodesLongFormOperandsStoreAndNextAddress()
    {
        var decoder = CreateDecoder(5, 0x14, 2, 3, 0x10);

        var instruction = decoder.Decode(0x500);

        Assert.Equal(ZMachineInstructionForm.Long, instruction.Form);
        Assert.Equal(20, instruction.Opcode);
        Assert.Collection(instruction.Operands,
            operand => Assert.Equal(new ZMachineOperand(ZMachineOperandType.SmallConstant, 2), operand),
            operand => Assert.Equal(new ZMachineOperand(ZMachineOperandType.SmallConstant, 3), operand));
        Assert.Equal((byte)0x10, instruction.StoreVariable);
        Assert.Equal(0x504, instruction.NextAddress);
    }

    [Fact]
    public void DecodesShortFormVariableOperandAndShortBranch()
    {
        var instruction = CreateDecoder(3, 0xa0, 0x21, 0xc5).Decode(0x500);

        Assert.Equal(ZMachineInstructionForm.Short, instruction.Form);
        Assert.Equal(0, instruction.Opcode);
        Assert.Equal(new ZMachineOperand(ZMachineOperandType.Variable, 0x21), Assert.Single(instruction.Operands));
        Assert.Equal(new ZMachineBranch(true, 5), instruction.Branch);
        Assert.Equal(0x503, instruction.NextAddress);
    }

    [Fact]
    public void DecodesVariableTwoOperandLargeConstant()
    {
        var instruction = CreateDecoder(5, 0xc8, 0x1f, 0x12, 0x34, 0x56, 0x10).Decode(0x500);

        Assert.Equal(ZMachineInstructionForm.Variable, instruction.Form);
        Assert.Equal(8, instruction.Opcode);
        Assert.Equal(new ZMachineOperand(ZMachineOperandType.LargeConstant, 0x1234), instruction.Operands[0]);
        Assert.Equal(new ZMachineOperand(ZMachineOperandType.SmallConstant, 0x56), instruction.Operands[1]);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesVariableJeWithThreeComparands()
    {
        var instruction = CreateDecoder(5, 0xc1, 0x57, 1, 2, 3, 0xc1).Decode(0x500);

        Assert.Equal(3, instruction.OperandCount);
        Assert.Equal(new ZMachineBranch(true, 1), instruction.Branch);
    }

    [Fact]
    public void EnforcesVersionSpecificTwoOperandCounts()
    {
        var v5 = CreateDecoder(5, 0xdb, 0x57, 1, 2, 3);
        var v6 = CreateDecoder(6, 0xdb, 0x57, 1, 2, 3).Decode(0x500);

        Assert.Throws<ZMachineRuntimeException>(() => v5.Decode(0x500));
        Assert.Equal(3, v6.OperandCount);
    }

    [Fact]
    public void DecodesDoubleVariableTypeBytesWithEightOperands()
    {
        var instruction = CreateDecoder(5, 0xec, 0x55, 0x55, 1, 2, 3, 4, 5, 6, 7, 8, 0x10).Decode(0x500);

        Assert.Equal(12, instruction.Opcode);
        Assert.Equal(8, instruction.OperandCount);
        Assert.Equal(Enumerable.Range(1, 8).Select(value => (ushort)value), instruction.Operands.Select(operand => operand.EncodedValue));
        Assert.Equal((byte)0x10, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesExtendedOpcodeAndSignedLongBranchOffset()
    {
        var instruction = CreateDecoder(6, 0xbe, 6, 0x5f, 1, 2, 0x20, 0x00).Decode(0x500);

        Assert.Equal(ZMachineInstructionForm.Extended, instruction.Form);
        Assert.Equal(6, instruction.Opcode);
        Assert.Equal(new ZMachineBranch(false, -8192), instruction.Branch);
        Assert.Equal(0x507, instruction.NextAddress);
    }

    [Fact]
    public void DecodesVersionSixBufferScreenStore()
    {
        var instruction = CreateDecoder(6, 0xbe, 29, 0x7f, 1, 0x10).Decode(0x500);

        Assert.Equal(29, instruction.Opcode);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesPrintInlineTextAndAdvancesPastTerminatingWord()
    {
        var story = MachineStoryFactory.Create(3);
        story[0x500] = 0xb2;
        var loaded = StoryLoader.Load(story);
        var memory = new ZMachineMemory(loaded);
        var codec = new ZMachineTextCodec(memory, loaded.Header);
        codec.EncodeZText("abc"u8).CopyTo(story, 0x501);
        loaded = StoryLoader.Load(story);
        memory = new ZMachineMemory(loaded);

        var instruction = new ZMachineInstructionDecoder(memory, loaded.Header).Decode(0x500);

        Assert.Equal("abc", instruction.EmbeddedText);
        Assert.Equal(0x503, instruction.NextAddress);
    }

    [Fact]
    public void DecodesInlineTextBeyondTheSixteenBitDataAddressRange()
    {
        const int address = 0x11000;
        var story = MachineStoryFactory.Create(5, 70000);
        story[address] = 0xb2;
        var loaded = StoryLoader.Load(story);
        var memory = new ZMachineMemory(loaded);
        new ZMachineTextCodec(memory, loaded.Header).EncodeZText("high"u8).CopyTo(story, address + 1);
        loaded = StoryLoader.Load(story);

        var instruction = new ZMachineInstructionDecoder(new ZMachineMemory(loaded), loaded.Header).Decode(address);

        Assert.Equal("high", instruction.EmbeddedText);
        Assert.True(instruction.NextAddress > ushort.MaxValue);
    }

    [Fact]
    public void RejectsIllegalVersionAndMalformedOperandTypes()
    {
        var v4Extended = CreateDecoder(4, 0xbe, 2, 0x5f, 1, 2);
        var malformed = CreateDecoder(5, 0xe8, 0xc0);
        var invalidOperandCount = CreateDecoder(4, 0xe4, 0x57, 1, 2, 3);

        Assert.Throws<ZMachineRuntimeException>(() => v4Extended.Decode(0x500));
        Assert.Throws<ZMachineRuntimeException>(() => malformed.Decode(0x500));
        Assert.Throws<ZMachineRuntimeException>(() => invalidOperandCount.Decode(0x500));
    }

    [Fact]
    public void RejectsTruncatedOperandsAndUndefinedOpcodes()
    {
        var truncatedStory = MachineStoryFactory.Create(5);
        truncatedStory[^1] = 0x14;
        var truncatedLoaded = StoryLoader.Load(truncatedStory);
        var truncated = new ZMachineInstructionDecoder(new ZMachineMemory(truncatedLoaded), truncatedLoaded.Header);
        var undefined = CreateDecoder(5, 0x1d, 1, 2);

        Assert.Throws<ZMachineRuntimeException>(() => truncated.Decode(truncatedStory.Length - 1));
        Assert.Throws<ZMachineRuntimeException>(() => undefined.Decode(0x500));
    }

    [Theory]
    [InlineData(1, 0xb5, 0x80, false, true)]
    [InlineData(4, 0xb5, 0x10, true, false)]
    [InlineData(5, 0xb9, 0x10, true, false)]
    public void AppliesVersionSpecificZeroOperandStoreAndBranchLayouts(byte version, byte opcode, byte operand, bool hasStore, bool hasBranch)
    {
        var decoder = CreateDecoder(version, opcode, operand);

        var instruction = decoder.Decode(0x500);

        Assert.Equal(hasStore, instruction.StoreVariable.HasValue);
        Assert.Equal(hasBranch, instruction.Branch is not null);
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
    public void DecodesCommonZeroOperandInstructionsInEveryVersion(byte version)
    {
        var instruction = CreateDecoder(version, 0xb0).Decode(0x500);

        Assert.Equal(ZMachineInstructionForm.Short, instruction.Form);
        Assert.Equal(0, instruction.OperandCount);
        Assert.Equal(0x501, instruction.NextAddress);
    }

    private static ZMachineInstructionDecoder CreateDecoder(byte version, params byte[] instruction)
    {
        var story = MachineStoryFactory.Create(version);
        instruction.CopyTo(story, 0x500);
        var loaded = StoryLoader.Load(story);
        var memory = new ZMachineMemory(loaded);
        return new ZMachineInstructionDecoder(memory, loaded.Header);
    }
}
