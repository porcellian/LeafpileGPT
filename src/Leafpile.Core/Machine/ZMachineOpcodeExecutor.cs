namespace Leafpile.Core.Machine;

/// <summary>Executes control-flow and routine opcodes for one decoded instruction.</summary>
public sealed class ZMachineOpcodeExecutor
{
    private readonly ZMachineState _state;
    private readonly IZMachineSaveRestoreService? _saveRestore;
    private readonly IZMachineObjectQueries? _objects;

    public ZMachineOpcodeExecutor(
        ZMachineState state,
        IZMachineSaveRestoreService? saveRestore = null,
        IZMachineObjectQueries? objects = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        _saveRestore = saveRestore;
        _objects = objects;
    }

    public ZMachineExecutionResult ExecuteNext()
    {
        var instruction = _state.Instructions.Decode(_state.ProgramCounter);
        return Execute(instruction);
    }

    public ZMachineExecutionResult Execute(ZMachineInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Address != _state.ProgramCounter)
        {
            throw new ZMachineRuntimeException($"Instruction at 0x{instruction.Address:x} cannot execute while the program counter is 0x{_state.ProgramCounter:x}.");
        }

        _state.SetProgramCounter(instruction.NextAddress);

        if (instruction.OpcodeClass == ZMachineOpcodeClass.ZeroOperand && instruction.Opcode is 2 or 3)
        {
            var printedText = instruction.Opcode == 3 ? $"{instruction.EmbeddedText}\n" : instruction.EmbeddedText;
            if (instruction.Opcode == 3)
            {
                Return(1);
            }

            return new ZMachineExecutionResult(Output: printedText);
        }

        return instruction.OpcodeClass switch
        {
            ZMachineOpcodeClass.TwoOperand => ExecuteTwoOperand(instruction),
            ZMachineOpcodeClass.OneOperand => ExecuteOneOperand(instruction),
            ZMachineOpcodeClass.ZeroOperand => ExecuteZeroOperand(instruction),
            ZMachineOpcodeClass.Variable => ExecuteVariable(instruction),
            ZMachineOpcodeClass.Extended => ExecuteExtended(instruction),
            _ => throw new ZMachineRuntimeException($"Unsupported opcode class {instruction.OpcodeClass}.")
        };
    }

    private ZMachineExecutionResult ExecuteTwoOperand(ZMachineInstruction instruction)
    {
        var operands = instruction.Operands;
        switch (instruction.Opcode)
        {
            case 1:
                {
                    var left = ReadValue(operands[0]);
                    var matched = false;
                    for (var index = 1; index < operands.Count; index++)
                    {
                        matched |= left == ReadValue(operands[index]);
                    }

                    Branch(instruction, matched);
                    break;
                }
            case 2:
                Branch(instruction, (short)ReadValue(operands[0]) < (short)ReadValue(operands[1]));
                break;
            case 3:
                Branch(instruction, (short)ReadValue(operands[0]) > (short)ReadValue(operands[1]));
                break;
            case 4:
            case 5:
                {
                    var variable = ReadVariableReference(operands[0]);
                    var value = variable == 0 ? _state.CallStack.Pop() : _state.ReadVariableValue(variable);
                    var updated = unchecked((ushort)(value + (instruction.Opcode == 4 ? -1 : 1)));
                    _state.WriteVariable(variable, updated);
                    var compare = ReadValue(operands[1]);
                    Branch(instruction, instruction.Opcode == 4 ? (short)updated < (short)compare : (short)updated > (short)compare);
                    break;
                }
            case 6:
                Branch(instruction, RequireObjects().IsChild(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 7:
                {
                    var bitmap = ReadValue(operands[0]);
                    var flags = ReadValue(operands[1]);
                    Branch(instruction, (bitmap & flags) == flags);
                    break;
                }
            case 10:
                Branch(instruction, RequireObjects().HasAttribute(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 25:
                Call(instruction, storeResult: true);
                break;
            case 26:
                Call(instruction, storeResult: false);
                break;
            case 28:
                {
                    var value = ReadValue(operands[0]);
                    var stackFrame = ReadValue(operands[1]);
                    ApplyReturn(_state.CallStack.Throw(value, stackFrame));
                    break;
                }
            default:
                throw Unsupported(instruction);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteOneOperand(ZMachineInstruction instruction)
    {
        var operand = instruction.Operands[0];
        switch (instruction.Opcode)
        {
            case 0:
                Branch(instruction, ReadValue(operand) == 0);
                break;
            case 1:
            case 2:
                {
                    var objectNumber = ReadValue(operand);
                    var value = instruction.Opcode == 1
                        ? RequireObjects().GetSibling(objectNumber)
                        : RequireObjects().GetChild(objectNumber);
                    StoreResult(instruction, value);
                    Branch(instruction, value != 0);
                    break;
                }
            case 8:
                Call(instruction, storeResult: true);
                break;
            case 11:
                Return(ReadValue(operand));
                break;
            case 12:
                {
                    var offset = (short)ReadValue(operand);
                    _state.SetProgramCounter(checked(instruction.NextAddress + offset - 2));
                    break;
                }
            case 15 when _state.Header.Version >= 5:
                Call(instruction, storeResult: false);
                break;
            default:
                throw Unsupported(instruction);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteZeroOperand(ZMachineInstruction instruction)
    {
        switch (instruction.Opcode)
        {
            case 0:
                Return(1);
                break;
            case 1:
                Return(0);
                break;
            case 4:
                break;
            case 5:
                return ExecuteSave(instruction, undo: false);
            case 6:
                return ExecuteRestore(instruction, undo: false);
            case 7:
                _state.Restart();
                return new ZMachineExecutionResult(Restarted: true);
            case 8:
                Return(_state.CallStack.Pop());
                break;
            case 9 when _state.Header.Version <= 4:
                _ = _state.CallStack.Pop();
                break;
            case 9:
                StoreResult(instruction, _state.CallStack.Catch());
                break;
            case 10:
                return new ZMachineExecutionResult(Halted: true);
            case 13:
                Branch(instruction, VerifyStory());
                break;
            case 15:
                Branch(instruction, condition: true);
                break;
            default:
                throw Unsupported(instruction);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteVariable(ZMachineInstruction instruction)
    {
        switch (instruction.Opcode)
        {
            case 0:
            case 12:
            case 25:
            case 26:
                Call(instruction, storeResult: instruction.Opcode is 0 or 12);
                break;
            case 8:
                _state.CallStack.Push(ReadValue(instruction.Operands[0]));
                break;
            case 9 when _state.Header.Version < 6:
                _state.WriteVariable(ReadVariableReference(instruction.Operands[0]), _state.CallStack.Pop());
                break;
            case 9:
                StoreResult(instruction, _state.CallStack.PullUserStack(ReadValue(instruction.Operands[0])));
                break;
            case 23:
                ExecuteScanTable(instruction);
                break;
            case 31:
                Branch(instruction, _state.CallStack.HasArgument(ReadValue(instruction.Operands[0])));
                break;
            default:
                throw Unsupported(instruction);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteExtended(ZMachineInstruction instruction)
    {
        switch (instruction.Opcode)
        {
            case 0:
            case 1:
                return instruction.Opcode == 0
                    ? ExecuteSave(instruction, undo: false)
                    : ExecuteRestore(instruction, undo: false);
            case 9:
                return ExecuteSave(instruction, undo: true);
            case 10:
                return ExecuteRestore(instruction, undo: true);
            case 24:
                {
                    var value = ReadValue(instruction.Operands[0]);
                    var stackAddress = ReadValue(instruction.Operands[1]);
                    var succeeded = _state.CallStack.PushUserStack(value, stackAddress);
                    Branch(instruction, succeeded);
                    break;
                }
            default:
                throw Unsupported(instruction);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteSave(ZMachineInstruction instruction, bool undo)
    {
        var result = RequireSaveRestoreService().Save(_state, instruction, undo);
        if (result.ResumedFromSavedState)
        {
            return new ZMachineExecutionResult(Restored: true);
        }

        if (_state.Header.Version <= 3)
        {
            Branch(instruction, result.Succeeded);
        }
        else
        {
            StoreResult(instruction, result.ReturnValue);
        }

        return new ZMachineExecutionResult();
    }

    private ZMachineExecutionResult ExecuteRestore(ZMachineInstruction instruction, bool undo)
    {
        var result = RequireSaveRestoreService().Restore(_state, instruction, undo);
        if (result.ResumedFromSavedState)
        {
            return new ZMachineExecutionResult(Restored: true);
        }

        if (_state.Header.Version <= 3)
        {
            // A successful restore resumes from the saved state; failure falls through.
            return new ZMachineExecutionResult();
        }

        StoreResult(instruction, result.ReturnValue);
        return new ZMachineExecutionResult();
    }

    private void Call(ZMachineInstruction instruction, bool storeResult)
    {
        var routine = ReadValue(instruction.Operands[0]);
        var arguments = new ushort[instruction.Operands.Count - 1];
        for (var index = 1; index < instruction.Operands.Count; index++)
        {
            arguments[index - 1] = ReadValue(instruction.Operands[index]);
        }

        var destination = storeResult ? instruction.StoreVariable : null;
        var call = _state.CallStack.CallRoutine(routine, arguments, instruction.NextAddress, destination);
        if (!call.RoutineStarted && destination.HasValue)
        {
            _state.WriteVariable(destination.Value, call.ImmediateReturnValue ?? 0);
        }
    }

    private void Return(ushort value) => ApplyReturn(_state.CallStack.ReturnFromRoutine(value));

    private void ApplyReturn(RoutineReturnResult result)
    {
        if (result.StoreVariable.HasValue)
        {
            _state.WriteVariable(result.StoreVariable.Value, result.Value);
        }
    }

    private void Branch(ZMachineInstruction instruction, bool condition)
    {
        var branch = instruction.Branch ?? throw new ZMachineRuntimeException($"Instruction at 0x{instruction.Address:x} has no branch data.");
        if (condition != branch.BranchOnTrue)
        {
            return;
        }

        if (branch.ReturnsFromRoutine)
        {
            Return(branch.ReturnValue);
            return;
        }

        _state.SetProgramCounter(checked(instruction.NextAddress + branch.Offset - 2));
    }

    private void ExecuteScanTable(ZMachineInstruction instruction)
    {
        var value = ReadValue(instruction.Operands[0]);
        var table = ReadValue(instruction.Operands[1]);
        var entryCount = ReadValue(instruction.Operands[2]);
        var form = instruction.Operands.Count == 4 ? ReadValue(instruction.Operands[3]) : (ushort)0x82;
        var isWord = (form & 0x80) != 0;
        var fieldLength = form & 0x7f;
        var foundAddress = (ushort)0;
        if (fieldLength != 0)
        {
            for (var index = 0; index < entryCount; index++)
            {
                var address = checked((int)table + (index * fieldLength));
                var candidate = isWord
                    ? _state.Memory.ReadWord(checked((ushort)address))
                    : _state.Memory.ReadByte(checked((ushort)address));
                if (candidate == value)
                {
                    foundAddress = checked((ushort)address);
                    break;
                }
            }
        }

        StoreResult(instruction, foundAddress);
        Branch(instruction, foundAddress != 0);
    }

    private bool VerifyStory()
    {
        var image = _state.Memory.GetStoryImage().Span;
        uint checksum = 0;
        for (var address = 0x40; address < _state.Header.FileLength; address++)
        {
            checksum = (checksum + image[address]) & 0xffff;
        }

        return checksum == _state.Header.Checksum;
    }

    private ushort ReadValue(ZMachineOperand operand) => operand.Type switch
    {
        ZMachineOperandType.Variable => _state.ReadVariableValue(checked((byte)operand.EncodedValue)),
        ZMachineOperandType.LargeConstant or ZMachineOperandType.SmallConstant => operand.EncodedValue,
        _ => throw new ZMachineRuntimeException($"Unknown operand type {operand.Type}.")
    };

    private byte ReadVariableReference(ZMachineOperand operand)
    {
        var value = operand.Type == ZMachineOperandType.Variable
            ? _state.ReadVariableValue(checked((byte)operand.EncodedValue))
            : operand.EncodedValue;
        if (value > byte.MaxValue)
        {
            throw new ZMachineRuntimeException($"Variable reference {value} is outside the range 0–255.");
        }

        return (byte)value;
    }

    private void StoreResult(ZMachineInstruction instruction, ushort value)
    {
        if (instruction.StoreVariable is not byte variable)
        {
            throw new ZMachineRuntimeException($"Instruction at 0x{instruction.Address:x} has no store variable.");
        }

        _state.WriteVariable(variable, value);
    }

    private IZMachineSaveRestoreService RequireSaveRestoreService() =>
        _saveRestore ?? throw new ZMachineRuntimeException("A save and restore service is required to execute this opcode.");

    private IZMachineObjectQueries RequireObjects() =>
        _objects ?? throw new ZMachineRuntimeException("An object-table query service is required to execute this opcode.");

    private static ZMachineRuntimeException Unsupported(ZMachineInstruction instruction) =>
        new($"Opcode class {instruction.OpcodeClass}, opcode {instruction.Opcode} is not implemented by the control-flow executor.");
}
