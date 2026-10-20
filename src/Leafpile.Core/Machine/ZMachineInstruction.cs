namespace Leafpile.Core.Machine;

public enum ZMachineInstructionForm
{
    Long,
    Short,
    Variable,
    Extended
}

public enum ZMachineOpcodeClass
{
    TwoOperand,
    OneOperand,
    ZeroOperand,
    Variable,
    Extended
}

public enum ZMachineOperandType
{
    LargeConstant,
    SmallConstant,
    Variable
}

public sealed record ZMachineOperand(ZMachineOperandType Type, ushort EncodedValue);

public sealed record ZMachineBranch(bool BranchOnTrue, short Offset)
{
    public bool ReturnsFromRoutine => Offset is 0 or 1;

    public ushort ReturnValue => Offset == 1 ? (ushort)1 : (ushort)0;
}

public sealed record ZMachineInstruction(
    int Address,
    int NextAddress,
    ZMachineInstructionForm Form,
    ZMachineOpcodeClass OpcodeClass,
    int Opcode,
    int OperandCount,
    IReadOnlyList<ZMachineOperand> Operands,
    byte? StoreVariable,
    ZMachineBranch? Branch,
    string? EmbeddedText,
    int? BranchAddress = null);
