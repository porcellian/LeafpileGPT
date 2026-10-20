using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Decodes Z-machine instruction bytes without executing the decoded instruction.</summary>
public sealed class ZMachineInstructionDecoder
{
    private readonly ZMachineMemory _memory;
    private readonly StoryHeader _header;
    private readonly ZMachineTextCodec _text;

    public ZMachineInstructionDecoder(ZMachineMemory memory, StoryHeader header, ZMachineTextCodec? text = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        _memory = memory;
        _header = header;
        _text = text ?? new ZMachineTextCodec(memory, header);
    }

    public ZMachineInstruction Decode(int address)
    {
        var start = address;
        var first = ReadByte(ref address);
        var form = (first & 0xc0) switch
        {
            0x00 or 0x40 => ZMachineInstructionForm.Long,
            0x80 => ZMachineInstructionForm.Short,
            0xc0 => ZMachineInstructionForm.Variable,
            _ => throw InvalidInstruction($"Unknown opcode form byte 0x{first:x2}.")
        };
        var operands = new List<ZMachineOperand>(4);
        int opcode;
        int operandCount;
        ZMachineOpcodeClass opcodeClass;

        if (first == 0xbe && _header.Version >= 5)
        {
            form = ZMachineInstructionForm.Extended;
            opcodeClass = ZMachineOpcodeClass.Extended;
            opcode = ReadByte(ref address);
            operandCount = ReadVariableOperands(ref address, form, opcode, isTwoOperand: false, operands);
        }
        else if (form == ZMachineInstructionForm.Long)
        {
            opcodeClass = ZMachineOpcodeClass.TwoOperand;
            opcode = first & 0x1f;
            operandCount = 2;
            AddOperand(operands, (first & 0x40) == 0 ? ZMachineOperandType.SmallConstant : ZMachineOperandType.Variable, ref address);
            AddOperand(operands, (first & 0x20) == 0 ? ZMachineOperandType.SmallConstant : ZMachineOperandType.Variable, ref address);
        }
        else if (form == ZMachineInstructionForm.Short)
        {
            opcode = first & 0x0f;
            var operandType = (first >> 4) & 0x03;
            operandCount = operandType == 3 ? 0 : 1;
            opcodeClass = operandCount == 0 ? ZMachineOpcodeClass.ZeroOperand : ZMachineOpcodeClass.OneOperand;
            if (operandCount == 1)
            {
                AddOperand(operands, (ZMachineOperandType)operandType, ref address);
            }
        }
        else
        {
            opcodeClass = (first & 0x20) == 0 ? ZMachineOpcodeClass.TwoOperand : ZMachineOpcodeClass.Variable;
            opcode = first & 0x1f;
            operandCount = ReadVariableOperands(ref address, form, opcode, (first & 0x20) == 0, operands);
        }

        var descriptor = GetDescriptor(form, opcode, operandCount, form == ZMachineInstructionForm.Variable && (first & 0x20) == 0);
        byte? storeVariable = descriptor.HasStore ? ReadByte(ref address) : null;
        int? branchAddress = descriptor.HasBranch ? address : null;
        ZMachineBranch? branch = descriptor.HasBranch ? ReadBranch(ref address) : null;
        string? embeddedText = null;
        if (descriptor.HasEmbeddedText)
        {
            var decoded = _text.DecodeWithEndAddress(address);
            embeddedText = decoded.Text;
            address = decoded.NextAddress;
        }

        return new ZMachineInstruction(start, address, form, opcodeClass, opcode, operandCount, operands.AsReadOnly(), storeVariable, branch, embeddedText, branchAddress);
    }

    private int ReadVariableOperands(ref int address, ZMachineInstructionForm form, int opcode, bool isTwoOperand, List<ZMachineOperand> operands)
    {
        var firstTypes = ReadByte(ref address);
        var types = new List<ZMachineOperandType>(8);
        var omittedSeen = false;
        ReadTypes(firstTypes, types, ref omittedSeen);
        if (form == ZMachineInstructionForm.Variable && !isTwoOperand && opcode is 12 or 26)
        {
            var secondTypes = ReadByte(ref address);
            ReadTypes(secondTypes, types, ref omittedSeen);
        }

        foreach (var type in types)
        {
            AddOperand(operands, type, ref address);
        }

        return types.Count;
    }

    private void ReadTypes(byte typeByte, List<ZMachineOperandType> types, ref bool omittedSeen)
    {
        for (var index = 0; index < 4; index++)
        {
            var shift = 6 - (index * 2);
            var type = (typeByte >> shift) & 0x03;
            if (type == 3)
            {
                omittedSeen = true;
                continue;
            }

            if (omittedSeen)
            {
                throw InvalidInstruction("An operand type appears after an omitted operand.");
            }

            types.Add((ZMachineOperandType)type);
        }
    }

    private void AddOperand(List<ZMachineOperand> operands, ZMachineOperandType type, ref int address)
    {
        var value = type switch
        {
            ZMachineOperandType.LargeConstant => (ushort)((ReadByte(ref address) << 8) | ReadByte(ref address)),
            ZMachineOperandType.SmallConstant or ZMachineOperandType.Variable => ReadByte(ref address),
            _ => throw InvalidInstruction($"Unknown operand type {(int)type}.")
        };
        operands.Add(new ZMachineOperand(type, value));
    }

    private ZMachineBranch ReadBranch(ref int address)
    {
        var first = ReadByte(ref address);
        var branchOnTrue = (first & 0x80) != 0;
        short offset;
        if ((first & 0x40) != 0)
        {
            offset = (short)(first & 0x3f);
        }
        else
        {
            var value = ((first & 0x3f) << 8) | ReadByte(ref address);
            offset = (short)((value & 0x2000) != 0 ? value | unchecked((int)0xffffc000) : value);
        }

        return new ZMachineBranch(branchOnTrue, offset);
    }

    private byte ReadByte(ref int address)
    {
        var value = _memory.ReadInstructionByte(address);
        address++;
        return value;
    }

    private OpcodeDescriptor GetDescriptor(ZMachineInstructionForm form, int opcode, int operandCount, bool variableTwoOperand)
    {
        OpcodeDescriptor descriptor;
        if (form == ZMachineInstructionForm.Long || (form == ZMachineInstructionForm.Variable && variableTwoOperand))
        {
            descriptor = opcode switch
            {
                1 when form == ZMachineInstructionForm.Variable => new(2, 4, branch: true),
                >= 1 and <= 7 => new(2, 2, branch: true),
                8 or 9 => new(2, 2, store: true),
                10 => new(2, 2, branch: true),
                >= 11 and <= 14 => new(2, 2),
                >= 15 and <= 24 => new(2, 2, store: true),
                25 when _header.Version >= 4 => new(2, 2, store: true),
                26 when _header.Version >= 5 => new(2, 2),
                27 when _header.Version >= 5 => new(2, _header.Version == 6 ? 3 : 2),
                28 when _header.Version >= 5 => new(2, 2),
                _ => throw InvalidInstruction($"2OP opcode {opcode} is not defined in version {_header.Version}.")
            };
        }
        else if (form == ZMachineInstructionForm.Short && operandCount == 1)
        {
            descriptor = opcode switch
            {
                0 => new(1, 1, branch: true),
                1 or 2 => new(1, 1, store: true, branch: true),
                3 or 4 => new(1, 1, store: true),
                >= 5 and <= 7 => new(1, 1),
                8 when _header.Version >= 4 => new(1, 1, store: true),
                >= 9 and <= 13 => new(1, 1),
                14 => new(1, 1, store: true),
                15 when _header.Version <= 4 => new(1, 1, store: true),
                15 when _header.Version >= 5 => new(1, 1),
                _ => throw InvalidInstruction($"1OP opcode {opcode} is not defined in version {_header.Version}.")
            };
        }
        else if (form == ZMachineInstructionForm.Short)
        {
            descriptor = opcode switch
            {
                0 or 1 or 7 or 8 or 10 or 11 => new(0, 0),
                2 or 3 => new(0, 0, embeddedText: true),
                4 => new(0, 0),
                5 when _header.Version <= 3 => new(0, 0, branch: true),
                5 when _header.Version == 4 => new(0, 0, store: true),
                6 when _header.Version <= 3 => new(0, 0, branch: true),
                6 when _header.Version == 4 => new(0, 0, store: true),
                9 when _header.Version <= 4 => new(0, 0),
                9 when _header.Version >= 5 => new(0, 0, store: true),
                12 when _header.Version == 3 => new(0, 0),
                13 when _header.Version >= 3 => new(0, 0, branch: true),
                14 when _header.Version < 5 => throw InvalidInstruction("Extended opcodes require version 5 or later."),
                15 when _header.Version >= 5 => new(0, 0, branch: true),
                _ => throw InvalidInstruction($"0OP opcode {opcode} is not defined in version {_header.Version}.")
            };
        }
        else if (form == ZMachineInstructionForm.Variable)
        {
            descriptor = GetVariableDescriptor(opcode);
        }
        else
        {
            descriptor = GetExtendedDescriptor(opcode);
        }

        if (!descriptor.Accepts(operandCount))
        {
            throw InvalidInstruction($"Opcode {opcode} in {form} form does not accept {operandCount} encoded operands.");
        }

        return descriptor;
    }

    private OpcodeDescriptor GetVariableDescriptor(int opcode) => opcode switch
    {
        0 => new(1, 4, store: true),
        >= 1 and <= 3 => new(3, 3),
        4 when _header.Version <= 3 => new(2, 2),
        4 when _header.Version == 4 => new(2, 4, rejectedOperandCounts: [3]),
        4 when _header.Version >= 5 => new(2, 4, store: true, rejectedOperandCounts: [3]),
        5 or 6 or 8 => new(1, 1),
        7 => new(1, 1, store: true),
        9 => new(1, 1, store: _header.Version >= 6),
        10 or 11 when _header.Version >= 3 => new(1, 1),
        12 when _header.Version >= 4 => new(1, 8, store: true),
        13 when _header.Version >= 4 => new(1, 1),
        14 when _header.Version >= 4 => new(1, 1),
        15 when _header.Version >= 4 => new(2, _header.Version == 6 ? 3 : 2),
        16 when _header.Version >= 4 => new(1, 1),
        17 when _header.Version >= 4 => new(1, 1),
        18 when _header.Version >= 4 => new(1, 1),
        19 when _header.Version is 3 or 4 => new(1, 2),
        19 when _header.Version == 5 => new(1, 2),
        19 when _header.Version == 6 => new(1, 3),
        20 when _header.Version >= 3 => new(1, 1),
        21 when _header.Version == 3 => new(0, 3),
        21 when _header.Version >= 5 => new(0, 4),
        22 when _header.Version >= 4 => new(1, 3, store: true, rejectedOperandCounts: [2]),
        23 when _header.Version >= 4 => new(3, 4, store: true, branch: true),
        24 when _header.Version >= 5 => new(1, 1, store: true),
        25 when _header.Version >= 5 => new(1, 4),
        26 when _header.Version >= 5 => new(1, 8),
        27 when _header.Version >= 5 => new(2, 4),
        28 when _header.Version >= 5 => new(4, 4),
        29 when _header.Version >= 5 => new(3, 3),
        30 when _header.Version >= 5 => new(2, 4),
        31 when _header.Version >= 5 => new(1, 1, branch: true),
        _ => throw InvalidInstruction($"VAR opcode {opcode} is not defined in version {_header.Version}.")
    };

    private OpcodeDescriptor GetExtendedDescriptor(int opcode) => opcode switch
    {
        0 or 1 when _header.Version >= 5 => new(2, 4, store: true),
        2 or 3 when _header.Version >= 5 => new(2, 2, store: true),
        4 when _header.Version >= 5 => new(1, _header.Version == 6 ? 2 : 1, store: true),
        5 or 6 or 7 or 8 when _header.Version == 6 => opcode == 6 ? new(2, 2, branch: true) : new(3, 3),
        9 or 10 when _header.Version >= 5 => new(0, 0, store: true),
        11 when _header.Version >= 5 => new(1, 1),
        12 when _header.Version >= 5 => new(1, 1, store: true),
        13 when _header.Version >= 5 => new(2, _header.Version == 6 ? 3 : 2),
        16 when _header.Version == 6 => new(3, 3),
        17 or 18 when _header.Version == 6 => new(3, 3),
        19 when _header.Version == 6 => new(2, 2, store: true),
        20 when _header.Version == 6 => new(2, 2),
        21 when _header.Version == 6 => new(1, 2),
        22 or 23 when _header.Version == 6 => new(1, 1),
        24 when _header.Version == 6 => new(2, 2, branch: true),
        25 when _header.Version == 6 => new(3, 3),
        26 when _header.Version == 6 => new(1, 1),
        27 when _header.Version == 6 => new(2, 2, branch: true),
        28 when _header.Version == 6 => new(1, 1),
        29 when _header.Version == 6 => new(1, 1, store: true),
        _ => throw InvalidInstruction($"EXT opcode {opcode} is not defined in version {_header.Version}.")
    };

    private static ZMachineRuntimeException InvalidInstruction(string message) => new($"Invalid instruction: {message}");

    private sealed record OpcodeDescriptor(
        int MinimumOperands,
        int MaximumOperands,
        bool store = false,
        bool branch = false,
        bool embeddedText = false,
        int[]? rejectedOperandCounts = null)
    {
        public bool Accepts(int count) =>
            count >= MinimumOperands && count <= MaximumOperands && !(rejectedOperandCounts?.Contains(count) ?? false);

        public bool HasStore => store;

        public bool HasBranch => branch;

        public bool HasEmbeddedText => embeddedText;
    }
}
