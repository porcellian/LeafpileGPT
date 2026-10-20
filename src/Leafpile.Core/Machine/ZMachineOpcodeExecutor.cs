namespace Leafpile.Core.Machine;

/// <summary>Executes decoded control-flow, arithmetic, memory, and routine instructions.</summary>
public sealed class ZMachineOpcodeExecutor
{
    private readonly ZMachineState _state;
    private readonly IZMachineSaveRestoreService? _saveRestore;
    private readonly IZMachineObjectQueries? _objects;
    private readonly ZMachineTextParser _textParser;

    public ZMachineOpcodeExecutor(
        ZMachineState state,
        IZMachineSaveRestoreService? saveRestore = null,
        IZMachineObjectQueries? objects = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        _saveRestore = saveRestore;
        _objects = objects ?? new ZMachineObjectTable(state);
        _textParser = new ZMachineTextParser(
            state.Memory,
            state.Header,
            new ZMachineDictionary(state.Memory, state.Header, state.Characters),
            state.Text);
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
                    var value = _state.ReadIndirectVariable(variable);
                    var updated = unchecked((ushort)(value + (instruction.Opcode == 4 ? -1 : 1)));
                    _state.WriteIndirectVariable(variable, updated);
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
            case 8:
                StoreResult(instruction, ZMachineArithmetic.Or(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 9:
                StoreResult(instruction, ZMachineArithmetic.And(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 10:
                Branch(instruction, RequireObjects().HasAttribute(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 11:
                RequireObjects().SetAttribute(ReadValue(operands[0]), ReadValue(operands[1]));
                break;
            case 12:
                RequireObjects().ClearAttribute(ReadValue(operands[0]), ReadValue(operands[1]));
                break;
            case 13:
                _state.WriteIndirectVariable(ReadVariableReference(operands[0]), ReadValue(operands[1]));
                break;
            case 14:
                RequireObjects().InsertObject(ReadValue(operands[0]), ReadValue(operands[1]));
                break;
            case 15:
            case 16:
                {
                    var array = ReadValue(operands[0]);
                    var index = ReadValue(operands[1]);
                    var address = CalculateAddress(array, index, instruction.Opcode == 15 ? sizeof(ushort) : sizeof(byte));
                    var value = instruction.Opcode == 15
                        ? _state.Memory.ReadWord(address)
                        : _state.Memory.ReadByte(address);
                    StoreResult(instruction, value);
                    break;
                }
            case 17:
                StoreResult(instruction, RequireObjects().GetProperty(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 18:
                StoreResult(instruction, RequireObjects().GetPropertyAddress(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 19:
                StoreResult(instruction, RequireObjects().GetNextProperty(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 20:
                StoreResult(instruction, ZMachineArithmetic.Add(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 21:
                StoreResult(instruction, ZMachineArithmetic.Subtract(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 22:
                StoreResult(instruction, ZMachineArithmetic.Multiply(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 23:
                StoreResult(instruction, ZMachineArithmetic.Divide(ReadValue(operands[0]), ReadValue(operands[1])));
                break;
            case 24:
                StoreResult(instruction, ZMachineArithmetic.Remainder(ReadValue(operands[0]), ReadValue(operands[1])));
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
            case 3:
                StoreResult(instruction, RequireObjects().GetParent(ReadValue(operand)));
                break;
            case 4:
                StoreResult(instruction, RequireObjects().GetPropertyLength(ReadValue(operand)));
                break;
            case 5:
            case 6:
                {
                    var variable = ReadVariableReference(operand);
                    var value = _state.ReadIndirectVariable(variable);
                    _state.WriteIndirectVariable(variable, unchecked((ushort)(value + (instruction.Opcode == 5 ? 1 : -1))));
                    break;
                }
            case 8:
                Call(instruction, storeResult: true);
                break;
            case 9:
                RequireObjects().RemoveObject(ReadValue(operand));
                break;
            case 10:
                return new ZMachineExecutionResult(Output: RequireObjects().GetShortName(ReadValue(operand)));
            case 11:
                Return(ReadValue(operand));
                break;
            case 12:
                {
                    var offset = (short)ReadValue(operand);
                    _state.SetProgramCounter(checked(instruction.NextAddress + offset - 2));
                    break;
                }
            case 14:
                StoreResult(instruction, _state.ReadIndirectVariable(ReadVariableReference(operand)));
                break;
            case 15 when _state.Header.Version <= 4:
                StoreResult(instruction, ZMachineArithmetic.Not(ReadValue(operand)));
                break;
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
                {
                    var variable = ReadVariableReference(instruction.Operands[0]);
                    var value = variable == 0 ? _state.CallStack.Peek() : _state.CallStack.Pop();
                    _state.WriteIndirectVariable(variable, value);
                    break;
                }
            case 9:
                StoreResult(instruction, _state.CallStack.PullUserStack(ReadValue(instruction.Operands[0])));
                break;
            case 3:
                RequireObjects().SetProperty(
                    ReadValue(instruction.Operands[0]),
                    ReadValue(instruction.Operands[1]),
                    ReadValue(instruction.Operands[2]));
                break;
            case 1:
            case 2:
                {
                    var array = ReadValue(instruction.Operands[0]);
                    var index = ReadValue(instruction.Operands[1]);
                    var value = ReadValue(instruction.Operands[2]);
                    var address = CalculateAddress(array, index, instruction.Opcode == 1 ? sizeof(ushort) : sizeof(byte));
                    if (instruction.Opcode == 1)
                    {
                        _state.Memory.WriteWord(address, value);
                    }
                    else
                    {
                        _state.Memory.WriteByte(address, (byte)value);
                    }

                    break;
                }
            case 7:
                StoreResult(instruction, _state.Random.Next(ReadValue(instruction.Operands[0])));
                break;
            case 23:
                ExecuteScanTable(instruction);
                break;
            case 27:
                {
                    var textBuffer = ReadValue(instruction.Operands[0]);
                    var parseBuffer = ReadValue(instruction.Operands[1]);
                    var dictionaryAddress = instruction.Operands.Count >= 3 ? ReadValue(instruction.Operands[2]) : (ushort)0;
                    var skipUnrecognized = instruction.Operands.Count == 4 && ReadValue(instruction.Operands[3]) != 0;
                    _textParser.Tokenise(
                        textBuffer,
                        parseBuffer,
                        dictionaryAddress == 0 ? _state.Header.DictionaryAddress : dictionaryAddress,
                        skipUnrecognized);
                    break;
                }
            case 28:
                {
                    var encoded = _textParser.EncodeText(
                        ReadValue(instruction.Operands[0]),
                        ReadValue(instruction.Operands[1]),
                        ReadValue(instruction.Operands[2]));
                    var destination = ReadValue(instruction.Operands[3]);
                    _state.Memory.ValidateDynamicRange(destination, encoded.Length);
                    for (var index = 0; index < encoded.Length; index++)
                    {
                        _state.Memory.WriteByte(checked((ushort)(destination + index)), encoded[index]);
                    }

                    break;
                }
            case 31:
                Branch(instruction, _state.CallStack.HasArgument(ReadValue(instruction.Operands[0])));
                break;
            case 29:
                ExecuteCopyTable(instruction);
                break;
            case 24 when _state.Header.Version >= 5:
                StoreResult(instruction, ZMachineArithmetic.Not(ReadValue(instruction.Operands[0])));
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
            case 21:
                _state.CallStack.PopStack(
                    ReadValue(instruction.Operands[0]),
                    instruction.Operands.Count == 2 ? ReadValue(instruction.Operands[1]) : null);
                break;
            case 2:
                StoreResult(instruction, Shift(ReadValue(instruction.Operands[0]), ReadValue(instruction.Operands[1]), arithmetic: false));
                break;
            case 3:
                StoreResult(instruction, Shift(ReadValue(instruction.Operands[0]), ReadValue(instruction.Operands[1]), arithmetic: true));
                break;
            case 12:
                StoreResult(instruction, _state.Characters.CheckUnicode(ReadValue(instruction.Operands[0])));
                break;
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

    private void ExecuteCopyTable(ZMachineInstruction instruction)
    {
        var source = ReadValue(instruction.Operands[0]);
        var destination = ReadValue(instruction.Operands[1]);
        var signedSize = (short)ReadValue(instruction.Operands[2]);
        if (signedSize == 0)
        {
            return;
        }

        var count = Math.Abs((int)signedSize);
        if (destination == 0)
        {
            _state.Memory.ValidateDynamicRange(source, count);
            for (var index = 0; index < count; index++)
            {
                _state.Memory.WriteByte(checked((ushort)(source + index)), 0);
            }

            return;
        }

        var direction = signedSize > 0 && destination > source && destination < (long)source + count ? -1 : 1;
        _state.Memory.ValidateReadableRange(source, count);
        _state.Memory.ValidateDynamicRange(destination, count);
        var indexToCopy = direction < 0 ? count - 1 : 0;
        for (var copied = 0; copied < count; copied++, indexToCopy += direction)
        {
            var value = _state.Memory.ReadByte(checked((ushort)(source + indexToCopy)));
            _state.Memory.WriteByte(checked((ushort)(destination + indexToCopy)), value);
        }
    }

    private ushort Shift(ushort value, ushort encodedPlaces, bool arithmetic)
    {
        var places = (short)encodedPlaces;
        if (places is < -15 or > 15)
        {
            throw new ZMachineRuntimeException($"Shift count {places} is outside the defined range -15 through 15.");
        }

        if (places >= 0)
        {
            return unchecked((ushort)(value << places));
        }

        return arithmetic
            ? unchecked((ushort)((short)value >> -places))
            : (ushort)(value >> -places);
    }

    private ushort CalculateAddress(ushort baseAddress, ushort index, int scale)
    {
        var address = (long)baseAddress + ((long)index * scale);
        var width = scale;
        if (address > ushort.MaxValue || address + width > _state.Memory.StaticMemoryLimitExclusive)
        {
            throw new ZMachineRuntimeException($"Array access at 0x{address:x} with width {width} is outside addressable memory.");
        }

        return (ushort)address;
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
        var value = operand.EncodedValue;
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
