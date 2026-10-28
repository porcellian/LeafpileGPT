namespace LeafpileGPT.Core;

using System.Collections.Immutable;

public enum InstructionForm
{
    Long,
    Short,
    Variable,
    Extended
}

public enum OpcodeClass
{
    ZeroOperand,
    OneOperand,
    TwoOperand,
    Variable,
    Extended
}

public enum OperandEncoding
{
    LargeConstant,
    SmallConstant,
    Variable
}

public sealed record DecodedOperand(OperandEncoding Encoding, ushort Value);

/// <summary>Version-specific metadata for one opcode form and number.</summary>
public sealed record OpcodeDefinition(
    OpcodeClass Class,
    byte Number,
    string Mnemonic,
    byte MinimumVersion,
    byte MaximumVersion,
    byte MinimumOperands,
    byte MaximumOperands,
    bool StoresResult = false,
    bool Branches = false,
    bool HasInlineText = false,
    bool IsIgnoredExtension = false)
{
    public bool SupportsVersion(byte version) => version >= MinimumVersion && version <= MaximumVersion;
}

public sealed record DecodedBranch(
    bool BranchOnTrue,
    short Offset,
    uint? TargetAddress,
    bool? ReturnValue);

/// <summary>A decoded instruction with its encoded operands and following address.</summary>
public sealed record DecodedInstruction(
    uint ProgramCounter,
    uint NextProgramCounter,
    InstructionForm Form,
    OpcodeClass Class,
    byte Opcode,
    OpcodeDefinition Definition,
    ImmutableArray<DecodedOperand> Operands,
    byte? StoreVariable,
    DecodedBranch? Branch,
    ImmutableArray<byte> InlineText);
