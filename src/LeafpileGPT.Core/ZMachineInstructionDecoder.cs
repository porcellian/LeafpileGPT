namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>Decodes Z-machine instruction encodings without executing their semantics.</summary>
public sealed class ZMachineInstructionDecoder
{
    private readonly ZMachineMemory _memory;

    public ZMachineInstructionDecoder(ZMachineMemory memory) =>
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public DecodedInstruction Decode(uint programCounter)
    {
        var version = _memory.Header.Version;
        var cursor = programCounter;
        var firstByte = ReadByte(ref cursor, programCounter);
        if (firstByte == 0xBE && version < 5)
        {
            throw new ZMachineInstructionException(programCounter, "The extended-opcode prefix is available only in versions 5 and later.");
        }

        var form = ResolveForm(firstByte);
        OpcodeClass opcodeClass;
        byte opcode;
        var operandTypes = new List<OperandEncoding>();
        var sawOmittedType = false;

        switch (form)
        {
            case InstructionForm.Long:
                opcodeClass = OpcodeClass.TwoOperand;
                opcode = (byte)(firstByte & 0x1F);
                operandTypes.Add((firstByte & 0x40) != 0 ? OperandEncoding.Variable : OperandEncoding.SmallConstant);
                operandTypes.Add((firstByte & 0x20) != 0 ? OperandEncoding.Variable : OperandEncoding.SmallConstant);
                break;
            case InstructionForm.Short:
                {
                    var type = (byte)((firstByte >> 4) & 0x03);
                    opcode = (byte)(firstByte & 0x0F);
                    opcodeClass = type == 3 ? OpcodeClass.ZeroOperand : OpcodeClass.OneOperand;
                    if (type != 3)
                    {
                        operandTypes.Add((OperandEncoding)type);
                    }

                    break;
                }
            case InstructionForm.Variable:
                {
                    opcodeClass = (firstByte & 0x20) == 0 ? OpcodeClass.TwoOperand : OpcodeClass.Variable;
                    opcode = (byte)(firstByte & 0x1F);
                    ReadOperandTypes(ref cursor, programCounter, operandTypes, ref sawOmittedType);
                    if (opcodeClass == OpcodeClass.Variable && opcode is 12 or 26)
                    {
                        ReadOperandTypes(ref cursor, programCounter, operandTypes, ref sawOmittedType);
                    }

                    break;
                }
            case InstructionForm.Extended:
                opcodeClass = OpcodeClass.Extended;
                opcode = ReadByte(ref cursor, programCounter);
                ReadOperandTypes(ref cursor, programCounter, operandTypes, ref sawOmittedType);
                break;
            default:
                throw new InvalidOperationException("Unknown instruction form.");
        }

        if (!ZMachineOpcodeTable.TryGet(opcodeClass, opcode, version, out var definition)
            && !(opcodeClass == OpcodeClass.Extended && opcode >= 29 && version >= 5
                && (definition = new OpcodeDefinition(OpcodeClass.Extended, opcode, $"ext_{opcode}", 5, 8, 0, 8, IsIgnoredExtension: true)) is not null))
        {
            throw new ZMachineInstructionException(programCounter, $"Opcode {opcodeClass}:{opcode} is not defined for version {version}.");
        }

        var operands = ImmutableArray.CreateBuilder<DecodedOperand>(operandTypes.Count);
        foreach (var type in operandTypes)
        {
            var value = type switch
            {
                OperandEncoding.LargeConstant => ReadWord(ref cursor, programCounter),
                OperandEncoding.SmallConstant or OperandEncoding.Variable => ReadByte(ref cursor, programCounter),
                _ => throw new ZMachineInstructionException(programCounter, $"Invalid operand encoding {type}.")
            };
            operands.Add(new DecodedOperand(type, value));
        }

        if (operands.Count < definition.MinimumOperands || operands.Count > definition.MaximumOperands)
        {
            throw new ZMachineInstructionException(programCounter,
                $"{definition.Mnemonic} requires {definition.MinimumOperands} to {definition.MaximumOperands} operands; found {operands.Count}.");
        }

        byte? storeVariable = definition.StoresResult ? ReadByte(ref cursor, programCounter) : null;
        DecodedBranch? branch = definition.Branches ? ReadBranch(ref cursor, programCounter) : null;
        var inlineText = definition.HasInlineText ? ReadInlineText(ref cursor, programCounter) : ImmutableArray<byte>.Empty;
        return new DecodedInstruction(programCounter, cursor, form, opcodeClass, opcode, definition,
            operands.MoveToImmutable(), storeVariable, branch, inlineText);
    }

    private static InstructionForm ResolveForm(byte firstByte) => firstByte switch
    {
        < 0x80 => InstructionForm.Long,
        0xBE => InstructionForm.Extended,
        < 0xC0 => InstructionForm.Short,
        < 0xE0 => InstructionForm.Variable,
        _ => InstructionForm.Variable
    };

    private void ReadOperandTypes(ref uint cursor, uint start, List<OperandEncoding> types, ref bool sawOmitted)
    {
        var typeByte = ReadByte(ref cursor, start);
        for (var shift = 6; shift >= 0; shift -= 2)
        {
            var type = (byte)((typeByte >> shift) & 0x03);
            if (type == 3)
            {
                sawOmitted = true;
                continue;
            }

            if (sawOmitted)
            {
                throw new ZMachineInstructionException(start, "An operand type follows an omitted operand.");
            }

            types.Add((OperandEncoding)type);
        }
    }

    private DecodedBranch ReadBranch(ref uint cursor, uint start)
    {
        var descriptorAddress = cursor;
        var first = ReadByte(ref cursor, start);
        var branchOnTrue = (first & 0x80) != 0;
        short offset;
        if ((first & 0x40) != 0)
        {
            offset = (short)(first & 0x3F);
        }
        else
        {
            var second = ReadByte(ref cursor, start);
            var encoded = (ushort)(((first & 0x3F) << 8) | second);
            offset = (short)((encoded & 0x2000) != 0 ? encoded | 0xC000 : encoded);
        }

        if (offset is 0 or 1)
        {
            return new DecodedBranch(branchOnTrue, offset, null, offset == 1,
                descriptorAddress, checked((byte)(cursor - descriptorAddress)));
        }

        var target = (long)cursor + offset - 2;
        if (target < 0 || target >= _memory.Length)
        {
            throw new ZMachineInstructionException(start, $"Branch target 0x{target:X} lies outside the story image.");
        }

        return new DecodedBranch(branchOnTrue, offset, (uint)target, null,
            descriptorAddress, checked((byte)(cursor - descriptorAddress)));
    }

    private ImmutableArray<byte> ReadInlineText(ref uint cursor, uint start)
    {
        var bytes = ImmutableArray.CreateBuilder<byte>();
        while (true)
        {
            var high = ReadByte(ref cursor, start);
            var low = ReadByte(ref cursor, start);
            bytes.Add(high);
            bytes.Add(low);
            if ((high & 0x80) != 0)
            {
                return bytes.ToImmutable();
            }
        }
    }

    private byte ReadByte(ref uint cursor, uint start)
    {
        try
        {
            var value = _memory.ReadStoryByte(cursor);
            cursor++;
            return value;
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineInstructionException(start, "Instruction is truncated or outside the story image.", exception);
        }
    }

    private ushort ReadWord(ref uint cursor, uint start)
    {
        var high = ReadByte(ref cursor, start);
        var low = ReadByte(ref cursor, start);
        return (ushort)((high << 8) | low);
    }
}

public sealed class ZMachineInstructionException : Exception
{
    public ZMachineInstructionException(uint programCounter, string message, Exception? innerException = null)
        : base($"Cannot decode instruction at 0x{programCounter:X}: {message}", innerException) => ProgramCounter = programCounter;

    public uint ProgramCounter { get; }
}
