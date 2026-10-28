namespace LeafpileGPT.Core;

/// <summary>Executes implemented arithmetic, object, data, stack, call, and control-flow opcodes.</summary>
public sealed class ZMachineOpcodeExecutor
{
    private readonly Action<string>? _writeText;

    public ZMachineOpcodeExecutor(Action<string>? writeText = null)
    {
        _writeText = writeText;
    }

    public void Execute(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(state);
        if (state.IsHalted)
        {
            throw new ZMachineExecutionException("Cannot execute an instruction after the machine has halted.");
        }

        if (instruction.Definition.Mnemonic == "quit")
        {
            state.Halt();
            return;
        }

        state.SetProgramCounter(instruction.NextProgramCounter);
        switch (instruction.Definition.Mnemonic)
        {
            case "add":
                StoreResult(instruction, state, unchecked((ushort)(ReadValue(instruction.Operands[0], state) + ReadValue(instruction.Operands[1], state))));
                break;
            case "sub":
                StoreResult(instruction, state, unchecked((ushort)(ReadValue(instruction.Operands[0], state) - ReadValue(instruction.Operands[1], state))));
                break;
            case "mul":
                StoreResult(instruction, state, unchecked((ushort)(ReadValue(instruction.Operands[0], state) * ReadValue(instruction.Operands[1], state))));
                break;
            case "div":
                StoreResult(instruction, state, Divide(instruction, state));
                break;
            case "mod":
                StoreResult(instruction, state, Remainder(instruction, state));
                break;
            case "and":
                StoreResult(instruction, state, (ushort)(ReadValue(instruction.Operands[0], state) & ReadValue(instruction.Operands[1], state)));
                break;
            case "or":
                StoreResult(instruction, state, (ushort)(ReadValue(instruction.Operands[0], state) | ReadValue(instruction.Operands[1], state)));
                break;
            case "not":
                StoreResult(instruction, state, unchecked((ushort)~ReadValue(instruction.Operands[0], state)));
                break;
            case "art_shift":
                StoreResult(instruction, state, Shift(instruction, state, arithmetic: true));
                break;
            case "log_shift":
                StoreResult(instruction, state, Shift(instruction, state, arithmetic: false));
                break;
            case "random":
                StoreResult(instruction, state, state.GetRandomNumber(unchecked((short)ReadValue(instruction.Operands[0], state))));
                break;
            case "je":
                ExecuteEqualBranch(instruction, state);
                break;
            case "jl":
                Branch(instruction, state, ToSigned(ReadValue(instruction.Operands[0], state)) < ToSigned(ReadValue(instruction.Operands[1], state)));
                break;
            case "jg":
                Branch(instruction, state, ToSigned(ReadValue(instruction.Operands[0], state)) > ToSigned(ReadValue(instruction.Operands[1], state)));
                break;
            case "jz":
                Branch(instruction, state, ReadValue(instruction.Operands[0], state) == 0);
                break;
            case "test":
                {
                    var bitmap = ReadValue(instruction.Operands[0], state);
                    var flags = ReadValue(instruction.Operands[1], state);
                    Branch(instruction, state, (bitmap & flags) == flags);
                    break;
                }
            case "jin":
                {
                    var objects = new ZMachineObjectTable(state);
                    Branch(instruction, state, objects.IsChildOf(
                        ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                    break;
                }
            case "test_attr":
                {
                    var objects = new ZMachineObjectTable(state);
                    Branch(instruction, state, objects.TestAttribute(
                        ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                    break;
                }
            case "set_attr":
            case "clear_attr":
                {
                    var objects = new ZMachineObjectTable(state);
                    objects.SetAttribute(ReadValue(instruction.Operands[0], state),
                        ReadValue(instruction.Operands[1], state), instruction.Definition.Mnemonic == "set_attr");
                    break;
                }
            case "insert_obj":
                new ZMachineObjectTable(state).InsertObject(
                    ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state));
                break;
            case "get_sibling":
                {
                    var sibling = new ZMachineObjectTable(state).GetSibling(ReadValue(instruction.Operands[0], state));
                    StoreResult(instruction, state, sibling);
                    Branch(instruction, state, sibling != 0);
                    break;
                }
            case "get_child":
                {
                    var child = new ZMachineObjectTable(state).GetChild(ReadValue(instruction.Operands[0], state));
                    StoreResult(instruction, state, child);
                    Branch(instruction, state, child != 0);
                    break;
                }
            case "get_parent":
                StoreResult(instruction, state, new ZMachineObjectTable(state).GetParent(ReadValue(instruction.Operands[0], state)));
                break;
            case "get_prop":
                StoreResult(instruction, state, new ZMachineObjectTable(state).GetProperty(
                    ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                break;
            case "get_prop_addr":
                StoreResult(instruction, state, new ZMachineObjectTable(state).GetPropertyAddress(
                    ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                break;
            case "get_next_prop":
                StoreResult(instruction, state, checked((ushort)new ZMachineObjectTable(state).GetNextProperty(
                    ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state))));
                break;
            case "get_prop_len":
                StoreResult(instruction, state, checked((ushort)new ZMachineObjectTable(state).GetPropertyLength(
                    ReadValue(instruction.Operands[0], state))));
                break;
            case "put_prop":
                new ZMachineObjectTable(state).SetProperty(ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state), ReadValue(instruction.Operands[2], state));
                break;
            case "remove_obj":
                new ZMachineObjectTable(state).RemoveObject(ReadValue(instruction.Operands[0], state));
                break;
            case "print_obj":
                if (_writeText is null)
                {
                    state.SetProgramCounter(instruction.ProgramCounter);
                    throw new ZMachineExecutionException("print_obj requires a configured text output callback.");
                }

                _writeText(new ZMachineObjectTable(state).GetShortName(ReadValue(instruction.Operands[0], state)));
                break;
            case "inc":
                MutateVariableReference(instruction, state, increment: true);
                break;
            case "dec":
                MutateVariableReference(instruction, state, increment: false);
                break;
            case "inc_chk":
            case "dec_chk":
                ExecuteCheckedMutation(instruction, state);
                break;
            case "store":
                state.WriteVariableReference(ResolveVariableNumber(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state));
                break;
            case "load":
                StoreResult(instruction, state, state.ReadVariableReference(ResolveVariableNumber(instruction.Operands[0], state)));
                break;
            case "loadb":
                StoreResult(instruction, state, ReadArrayByte(state, ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                break;
            case "loadw":
                StoreResult(instruction, state, ReadArrayWord(state, ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                break;
            case "storeb":
                WriteArrayByte(state, ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state), ReadValue(instruction.Operands[2], state));
                break;
            case "storew":
                WriteArrayWord(state, ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state), ReadValue(instruction.Operands[2], state));
                break;
            case "copy_table":
                CopyTable(state, ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state), unchecked((short)ReadValue(instruction.Operands[2], state)));
                break;
            case "scan_table":
                ExecuteScanTable(instruction, state);
                break;
            case "push":
                state.PushEvaluationStack(ReadValue(instruction.Operands[0], state));
                break;
            case "pop":
                _ = state.PopEvaluationStack();
                break;
            case "pull":
                ExecutePull(instruction, state);
                break;
            case "pop_stack":
                state.PopStack(ReadValue(instruction.Operands[0], state),
                    instruction.Operands.Length > 1 ? ReadValue(instruction.Operands[1], state) : (ushort)0);
                break;
            case "push_stack":
                Branch(instruction, state,
                    state.PushUserStack(ReadValue(instruction.Operands[0], state), ReadValue(instruction.Operands[1], state)));
                break;
            case "call":
            case "call_vs":
            case "call_vs2":
            case "call_1s":
            case "call_1n":
            case "call_2s":
            case "call_2n":
            case "call_vn":
            case "call_vn2":
                ExecuteCall(instruction, state);
                break;
            case "ret":
                state.ReturnFromRoutine(ReadValue(instruction.Operands[0], state));
                break;
            case "ret_popped":
                state.ReturnFromRoutine(state.PopEvaluationStack());
                break;
            case "rtrue":
                state.ReturnFromRoutine(1);
                break;
            case "rfalse":
                state.ReturnFromRoutine(0);
                break;
            case "jump":
                ExecuteJump(instruction, state);
                break;
            case "catch":
                StoreResult(instruction, state, state.CatchFrame());
                break;
            case "throw":
                state.ThrowToFrame(ReadValue(instruction.Operands[1], state), ReadValue(instruction.Operands[0], state));
                break;
            case "check_arg_count":
                Branch(instruction, state, state.HasArgument(checked((byte)ReadValue(instruction.Operands[0], state))));
                break;
            case "verify":
                Branch(instruction, state,
                    state.Memory.Header.ChecksumStatus == StoryChecksumStatus.Valid
                    && state.Memory.Header.StoredChecksum == state.Memory.Header.CalculatedChecksum);
                break;
            case "nop":
                break;
            default:
                state.SetProgramCounter(instruction.ProgramCounter);
                throw new ZMachineOpcodeNotImplementedException(
                    instruction.ProgramCounter,
                    instruction.Definition.Mnemonic,
                    $"{instruction.Definition.Mnemonic} is outside the arithmetic and execution operations implemented in Task 7.");
        }
    }

    private static ushort Divide(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var numerator = ToSigned(ReadValue(instruction.Operands[0], state));
        var denominator = ToSigned(ReadValue(instruction.Operands[1], state));
        EnsureNonzeroDivisor(instruction, state, denominator);
        return unchecked((ushort)(numerator / denominator));
    }

    private static ushort Remainder(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var numerator = ToSigned(ReadValue(instruction.Operands[0], state));
        var denominator = ToSigned(ReadValue(instruction.Operands[1], state));
        EnsureNonzeroDivisor(instruction, state, denominator);
        return unchecked((ushort)(numerator % denominator));
    }

    private static ushort Shift(DecodedInstruction instruction, ZMachineExecutionState state, bool arithmetic)
    {
        var value = ReadValue(instruction.Operands[0], state);
        var places = ToSigned(ReadValue(instruction.Operands[1], state));
        if (places is < -15 or > 15)
        {
            throw new ZMachineExecutionException($"Shift count {places} is outside the standard range -15 through 15.");
        }

        var shifted = places >= 0
            ? value << places
            : arithmetic
                ? (short)value >> -places
                : value >> -places;
        return unchecked((ushort)shifted);
    }

    private static void EnsureNonzeroDivisor(
        DecodedInstruction instruction,
        ZMachineExecutionState state,
        short denominator)
    {
        if (denominator == 0)
        {
            state.Halt();
            throw new ZMachineExecutionException($"Opcode {instruction.Definition.Mnemonic} cannot divide by zero.");
        }
    }

    private static void ExecuteEqualBranch(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var values = instruction.Operands.Select(operand => ReadValue(operand, state)).ToArray();
        var matches = values.Skip(1).Any(value => value == values[0]);
        Branch(instruction, state, matches);
    }

    private static void MutateVariableReference(DecodedInstruction instruction, ZMachineExecutionState state, bool increment)
    {
        var variableNumber = ResolveVariableNumber(instruction.Operands[0], state);
        var current = state.ReadVariableReference(variableNumber);
        var updated = unchecked((ushort)(increment ? current + 1 : current - 1));
        state.WriteVariableReference(variableNumber, updated);
    }

    private static void ExecuteCheckedMutation(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var variableNumber = ResolveVariableNumber(instruction.Operands[0], state);
        var current = state.ReadVariableReference(variableNumber);
        var comparison = ToSigned(ReadValue(instruction.Operands[1], state));
        var updated = unchecked((ushort)(instruction.Definition.Mnemonic == "inc_chk" ? current + 1 : current - 1));
        state.WriteVariableReference(variableNumber, updated);
        var result = ToSigned(updated);
        Branch(instruction, state, instruction.Definition.Mnemonic == "inc_chk" ? result > comparison : result < comparison);
    }

    private static void ExecuteCall(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var values = instruction.Operands.Select(operand => ReadValue(operand, state)).ToArray();
        var storesResult = instruction.Definition.StoresResult;
        state.CallRoutine(values[0], values.AsSpan(1), instruction.NextProgramCounter,
            storesResult ? instruction.StoreVariable : null);
    }

    private static void ExecuteJump(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var offset = ToSigned(ReadValue(instruction.Operands[0], state));
        var target = (long)instruction.NextProgramCounter + offset - 2;
        if (target < 0 || target >= state.Memory.Length)
        {
            throw new ZMachineExecutionException($"Jump target 0x{target:X} lies outside the story image.");
        }

        state.SetProgramCounter((uint)target);
    }

    private static void ExecutePull(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        if (state.Version >= 6)
        {
            StoreResult(instruction, state, state.PullStack(ReadValue(instruction.Operands[0], state)));
            return;
        }

        var variableNumber = ResolveVariableNumber(instruction.Operands[0], state);
        var value = state.PopEvaluationStack();
        state.WriteVariableReference(variableNumber, value);
    }

    private static void ExecuteScanTable(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var value = ReadValue(instruction.Operands[0], state);
        var tableAddress = ReadValue(instruction.Operands[1], state);
        var entryCount = ReadValue(instruction.Operands[2], state);
        var form = instruction.Operands.Length > 3 ? ReadValue(instruction.Operands[3], state) : (ushort)0x82;
        var wordEntries = (form & 0x80) != 0;
        var fieldLength = (ushort)(form & 0x7F);
        if (fieldLength == 0 || (wordEntries && fieldLength < sizeof(ushort)))
        {
            throw new ZMachineExecutionException($"scan_table form 0x{form:X2} has an invalid field width.");
        }

        var totalBytes = checked((int)entryCount * fieldLength);
        state.Memory.ValidateReadableRange(tableAddress, totalBytes);
        ushort foundAddress = 0;
        for (var index = 0; index < entryCount; index++)
        {
            var address = (uint)tableAddress + (uint)index * fieldLength;
            var entry = wordEntries
                ? state.Memory.ReadWord(checked((ushort)address))
                : state.Memory.ReadByte(checked((ushort)address));
            if (entry == value)
            {
                foundAddress = checked((ushort)address);
                break;
            }
        }

        StoreResult(instruction, state, foundAddress);
        Branch(instruction, state, foundAddress != 0);
    }

    private static byte ReadArrayByte(ZMachineExecutionState state, ushort baseAddress, ushort byteIndex)
    {
        var address = (uint)baseAddress + byteIndex;
        state.Memory.ValidateReadableRange(address, 1);
        return state.Memory.ReadByte(checked((ushort)address));
    }

    private static ushort ReadArrayWord(ZMachineExecutionState state, ushort baseAddress, ushort wordIndex)
    {
        var address = (uint)baseAddress + (uint)wordIndex * sizeof(ushort);
        state.Memory.ValidateReadableRange(address, sizeof(ushort));
        return state.Memory.ReadWord(checked((ushort)address));
    }

    private static void WriteArrayByte(ZMachineExecutionState state, ushort baseAddress, ushort byteIndex, ushort value)
    {
        var address = (uint)baseAddress + byteIndex;
        state.Memory.ValidateWritableRange(address, 1);
        state.Memory.WriteByte(checked((ushort)address), (byte)value);
    }

    private static void WriteArrayWord(ZMachineExecutionState state, ushort baseAddress, ushort wordIndex, ushort value)
    {
        var address = (uint)baseAddress + (uint)wordIndex * sizeof(ushort);
        state.Memory.ValidateWritableRange(address, sizeof(ushort));
        state.Memory.WriteWord(checked((ushort)address), value);
    }

    private static void CopyTable(ZMachineExecutionState state, ushort firstAddress, ushort secondAddress, short size)
    {
        var length = Math.Abs((int)size);
        if (length == 0)
        {
            return;
        }

        state.Memory.ValidateReadableRange(firstAddress, length);
        if (secondAddress == 0)
        {
            state.Memory.ValidateWritableRange(firstAddress, length);
            for (var index = 0; index < length; index++)
            {
                state.Memory.WriteByte((ushort)(firstAddress + index), 0);
            }

            return;
        }

        state.Memory.ValidateWritableRange(secondAddress, length);
        if (size < 0)
        {
            for (var index = 0; index < length; index++)
            {
                state.Memory.WriteByte((ushort)(secondAddress + index), state.Memory.ReadByte((ushort)(firstAddress + index)));
            }

            return;
        }

        var copy = new byte[length];
        for (var index = 0; index < length; index++)
        {
            copy[index] = state.Memory.ReadByte((ushort)(firstAddress + index));
        }

        for (var index = 0; index < length; index++)
        {
            state.Memory.WriteByte((ushort)(secondAddress + index), copy[index]);
        }
    }

    private static void Branch(DecodedInstruction instruction, ZMachineExecutionState state, bool condition)
    {
        var branch = instruction.Branch
            ?? throw new ZMachineExecutionException($"Opcode {instruction.Definition.Mnemonic} has no branch data.");
        if (condition != branch.BranchOnTrue)
        {
            return;
        }

        if (branch.ReturnValue is not null)
        {
            state.ReturnFromRoutine(branch.ReturnValue.Value ? (ushort)1 : (ushort)0);
        }
        else
        {
            state.SetProgramCounter(branch.TargetAddress
                ?? throw new ZMachineExecutionException("A taken branch has no target address."));
        }
    }

    private static void StoreResult(DecodedInstruction instruction, ZMachineExecutionState state, ushort value)
    {
        state.WriteVariable(instruction.StoreVariable
            ?? throw new ZMachineExecutionException($"Opcode {instruction.Definition.Mnemonic} has no store variable."), value);
    }

    private static ushort ReadValue(DecodedOperand operand, ZMachineExecutionState state) =>
        operand.Encoding == OperandEncoding.Variable
            ? state.ReadVariable(checked((byte)operand.Value))
            : operand.Value;

    private static byte ResolveVariableNumber(DecodedOperand operand, ZMachineExecutionState state)
    {
        // These instructions take a variable identifier. A variable-encoded
        // operand therefore names that variable; it is not an indirect read.
        var value = operand.Value;
        if (value > byte.MaxValue)
        {
            throw new ZMachineExecutionException($"Variable reference {value} is outside the range 0 through 255.");
        }

        return (byte)value;
    }

    private static short ToSigned(ushort value) => unchecked((short)value);
}

public sealed class ZMachineOpcodeNotImplementedException : Exception
{
    public ZMachineOpcodeNotImplementedException(uint programCounter, string mnemonic, string message)
        : base($"Opcode {mnemonic} at 0x{programCounter:X} is not implemented: {message}")
    {
        ProgramCounter = programCounter;
        Mnemonic = mnemonic;
    }

    public uint ProgramCounter { get; }

    public string Mnemonic { get; }
}
