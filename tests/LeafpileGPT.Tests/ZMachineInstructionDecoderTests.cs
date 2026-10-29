namespace LeafpileGPT.Tests;

using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineInstructionDecoderTests
{
    private const uint CodeAddress = 850;

    [Fact]
    public void DecodesLongFormOperandsAndStoreVariable()
    {
        var decoder = CreateDecoder(5, 0x14, 0x02, 0x03, 0x10);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(InstructionForm.Long, instruction.Form);
        Assert.Equal(OpcodeClass.TwoOperand, instruction.Class);
        Assert.Equal("add", instruction.Definition.Mnemonic);
        Assert.Equal((ushort)2, instruction.Operands[0].Value);
        Assert.Equal((ushort)3, instruction.Operands[1].Value);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
        Assert.Equal(CodeAddress + 4, instruction.NextProgramCounter);
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
    public void DecodesCommonInstructionAcrossEveryStoryVersion(byte version)
    {
        var decoder = CreateDecoder(version, 0xB0);

        var instruction = decoder.Decode(CodeAddress);

        Assert.True(instruction.Definition.SupportsVersion(version));
        Assert.Equal("rtrue", instruction.Definition.Mnemonic);
        Assert.Equal(CodeAddress + 1, instruction.NextProgramCounter);
    }

    [Fact]
    public void DecodesVariableTwoOperandFormAndShortBranchReturn()
    {
        var decoder = CreateDecoder(5, 0xC1, 0x5F, 0x04, 0x04, 0xC1);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(InstructionForm.Variable, instruction.Form);
        Assert.Equal(OpcodeClass.TwoOperand, instruction.Class);
        Assert.Equal("je", instruction.Definition.Mnemonic);
        Assert.Equal((ushort)4, instruction.Operands[0].Value);
        Assert.Equal((ushort)4, instruction.Operands[1].Value);
        Assert.Equal(true, instruction.Branch?.BranchOnTrue);
        Assert.Equal((short)1, instruction.Branch?.Offset);
        Assert.Equal(true, instruction.Branch?.ReturnValue);
        Assert.Null(instruction.Branch?.TargetAddress);
        Assert.Equal(CodeAddress + 4, instruction.Branch?.DescriptorAddress);
        Assert.Equal((byte)1, instruction.Branch?.EncodedLength);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DecodesMemoryStreamTableOperandInVersionsThreeThroughFive(byte version)
    {
        var decoder = CreateDecoder(version, 0xF3, 0x6F, 0x03, 0x9A);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal("output_stream", instruction.Definition.Mnemonic);
        Assert.Equal(2, instruction.Operands.Length);
        Assert.Equal((ushort)3, instruction.Operands[0].Value);
        Assert.Equal(OperandEncoding.Variable, instruction.Operands[1].Encoding);
        Assert.Equal((ushort)0x9A, instruction.Operands[1].Value);
    }

    [Fact]
    public void DecodesLargeAndVariableOperandsFromTypeByte()
    {
        var decoder = CreateDecoder(5, 0xC1, 0x2F, 0x12, 0x34, 0x07, 0xC1);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(OperandEncoding.LargeConstant, instruction.Operands[0].Encoding);
        Assert.Equal((ushort)0x1234, instruction.Operands[0].Value);
        Assert.Equal(OperandEncoding.Variable, instruction.Operands[1].Encoding);
        Assert.Equal((ushort)7, instruction.Operands[1].Value);
    }

    [Fact]
    public void DecodesStoreAndBranchOnTheSameInstruction()
    {
        var decoder = CreateDecoder(5, 0xF7, 0x57, 1, 2, 3, 0x10, 0xC1);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal("scan_table", instruction.Definition.Mnemonic);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
        Assert.Equal((short)1, instruction.Branch?.Offset);
        Assert.Equal(CodeAddress + 7, instruction.NextProgramCounter);
    }

    [Fact]
    public void DecodesOneOperandAndSignedLongBranchTarget()
    {
        var decoder = CreateDecoder(5, 0x90, 0x07, 0x3F, 0xFF);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(InstructionForm.Short, instruction.Form);
        Assert.Equal("jz", instruction.Definition.Mnemonic);
        Assert.Equal((short)-1, instruction.Branch?.Offset);
        Assert.Equal(CodeAddress + 1, instruction.Branch?.TargetAddress);
    }

    [Fact]
    public void DecodesInlineTextIncludingTerminatingWord()
    {
        var decoder = CreateDecoder(5, 0xB2, 0x12, 0x80, 0xD6, 0x00);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal("print", instruction.Definition.Mnemonic);
        Assert.Equal(new byte[] { 0x12, 0x80, 0xD6, 0x00 }, instruction.InlineText);
        Assert.Equal(CodeAddress + 5, instruction.NextProgramCounter);
    }

    [Fact]
    public void DecodesExtendedOpcodeAndStore()
    {
        var decoder = CreateDecoder(5, 0xBE, 0x02, 0x5F, 0x01, 0x02, 0x10);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(InstructionForm.Extended, instruction.Form);
        Assert.Equal(OpcodeClass.Extended, instruction.Class);
        Assert.Equal("log_shift", instruction.Definition.Mnemonic);
        Assert.Equal(2, instruction.Operands.Length);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
        Assert.Equal(CodeAddress + 6, instruction.NextProgramCounter);
    }

    [Fact]
    public void IgnoresUnassignedExtendedOpcodeNumbersAsSpecified()
    {
        var v5BufferScreenSlot = CreateDecoder(5, 0xBE, 29, 0xFF);
        var futureOpcode = CreateDecoder(8, 0xBE, 30, 0xFF);

        Assert.True(v5BufferScreenSlot.Decode(CodeAddress).Definition.IsIgnoredExtension);
        Assert.True(futureOpcode.Decode(CodeAddress).Definition.IsIgnoredExtension);
        Assert.Equal("buffer_screen", CreateDecoder(6, 0xBE, 29, 0x7F, 0x01, 0x10).Decode(CodeAddress).Definition.Mnemonic);
    }

    [Fact]
    public void DecodesSecondOperandTypeByteForCallVs2()
    {
        var decoder = CreateDecoder(5, 0xEC, 0x55, 0x6F, 1, 2, 3, 4, 5, 6, 0x10);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal("call_vs2", instruction.Definition.Mnemonic);
        Assert.Equal(InstructionForm.Variable, instruction.Form);
        Assert.Equal(6, instruction.Operands.Length);
        Assert.Equal(OperandEncoding.SmallConstant, instruction.Operands[4].Encoding);
        Assert.Equal(OperandEncoding.Variable, instruction.Operands[5].Encoding);
        Assert.Equal((ushort)5, instruction.Operands[4].Value);
        Assert.Equal((ushort)6, instruction.Operands[5].Value);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesDoubleTypeByteWhenFirstTypeByteEndsWithOmittedOperands()
    {
        var decoder = CreateDecoder(5, 0xEC, 0x7F, 0xFF, 0x12, 0x10);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Single(instruction.Operands);
        Assert.Equal((ushort)0x12, instruction.Operands[0].Value);
        Assert.Equal((byte)0x10, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesVersionSpecificNotAndCatchAliases()
    {
        var v4 = CreateDecoder(4, 0x9F, 0x12, 0x10);
        var v5 = CreateDecoder(5, 0x9F, 0x12);
        var v4CatchSlot = CreateDecoder(4, 0xB9);
        var v5CatchSlot = CreateDecoder(5, 0xB9, 0x10);

        Assert.Equal("not", v4.Decode(CodeAddress).Definition.Mnemonic);
        Assert.Equal("call_1n", v5.Decode(CodeAddress).Definition.Mnemonic);
        Assert.Equal("pop", v4CatchSlot.Decode(CodeAddress).Definition.Mnemonic);
        Assert.Equal("catch", v5CatchSlot.Decode(CodeAddress).Definition.Mnemonic);
    }

    [Fact]
    public void OpcodeTableCoversEveryStandardOpcodeAndVersionSpecificVariant()
    {
        Assert.Equal(119, ZMachineOpcodeTable.AllDefinitions.Select(definition => (definition.Class, definition.Number)).Distinct().Count());
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.ZeroOperand, 5, 3, out var v3Save));
        Assert.True(v3Save.Branches);
        Assert.False(v3Save.StoresResult);
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.ZeroOperand, 5, 4, out var v4Save));
        Assert.True(v4Save.StoresResult);
        Assert.False(v4Save.Branches);
        Assert.False(ZMachineOpcodeTable.TryGet(OpcodeClass.ZeroOperand, 5, 5, out _));
        Assert.False(ZMachineOpcodeTable.TryGet(OpcodeClass.Extended, 5, 5, out _));
        Assert.True(ZMachineOpcodeTable.TryGet(OpcodeClass.Extended, 5, 6, out _));
    }

    [Fact]
    public void RejectsUndefinedVersionedOpcode()
    {
        var decoder = CreateDecoder(4, 0xBE, 0x02, 0xFF);

        var exception = Assert.Throws<ZMachineInstructionException>(() => decoder.Decode(CodeAddress));

        Assert.Equal(CodeAddress, exception.ProgramCounter);
        Assert.Contains("available only in versions 5 and later", exception.Message);
    }

    [Fact]
    public void RejectsUndefinedOpcodeNumber()
    {
        var decoder = CreateDecoder(5, 0x00, 0, 0);

        Assert.Throws<ZMachineInstructionException>(() => decoder.Decode(CodeAddress));
    }

    [Fact]
    public void RejectsOperandTypeAfterOmittedType()
    {
        var decoder = CreateDecoder(5, 0xC1, 0x37);

        var exception = Assert.Throws<ZMachineInstructionException>(() => decoder.Decode(CodeAddress));

        Assert.Contains("follows an omitted operand", exception.Message);
    }

    [Fact]
    public void RejectsOperandTypeAfterOmittedInSecondTypeByte()
    {
        var decoder = CreateDecoder(5, 0xEC, 0x5F, 0x5F);

        var exception = Assert.Throws<ZMachineInstructionException>(() => decoder.Decode(CodeAddress));

        Assert.Contains("follows an omitted operand", exception.Message);
    }

    [Fact]
    public void RejectsTruncatedOperandOrInlineText()
    {
        var operandDecoder = CreateDecoder(5, atEnd: true, 0x14, 0x01);
        var textDecoder = CreateDecoder(5, atEnd: true, 0xB2, 0x12);

        Assert.Throws<ZMachineInstructionException>(() => operandDecoder.Decode(1022));
        Assert.Throws<ZMachineInstructionException>(() => textDecoder.Decode(1022));
    }

    [Fact]
    public void CalculatesBranchTargetRelativeToAddressAfterBranchBytes()
    {
        var decoder = CreateDecoder(5, 0x90, 0x01, 0x00, 0x05);

        var instruction = decoder.Decode(CodeAddress);

        Assert.Equal(CodeAddress + 7, instruction.Branch?.TargetAddress);
        Assert.Equal(CodeAddress + 4, instruction.NextProgramCounter);
    }

    private static ZMachineInstructionDecoder CreateDecoder(byte version, params byte[] code) =>
        CreateDecoder(version, false, code);

    private static ZMachineInstructionDecoder CreateDecoder(byte version, bool atEnd, params byte[] code)
    {
        var storyBytes = StoryFixtureBuilder.Create(version);
        var codeAddress = atEnd ? storyBytes.Length - code.Length : checked((int)CodeAddress);
        code.CopyTo(storyBytes, codeAddress);
        if (version >= 3)
        {
            StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        }

        return new ZMachineInstructionDecoder(new ZMachineMemory(StoryFile.Parse(storyBytes)));
    }
}
