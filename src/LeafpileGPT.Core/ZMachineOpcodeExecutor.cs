namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

/// <summary>Executes implemented arithmetic, object, data, stack, call, and control-flow opcodes.</summary>
public sealed class ZMachineOpcodeExecutor
{
    private readonly Action<string>? _writeText;

    public ZMachineOpcodeExecutor(Action<string>? writeText = null)
    {
        _writeText = writeText;
    }

    /// <summary>Executes input opcodes asynchronously; all other opcodes use the synchronous executor.</summary>
    public async ValueTask ExecuteAsync(
        DecodedInstruction instruction,
        ZMachineExecutionState state,
        IZMachineInputProvider inputProvider,
        Func<ushort, CancellationToken, ValueTask<bool>>? timedRoutine = null,
        Action? redrawInput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputProvider);
        var mnemonic = instruction.Definition.Mnemonic;
        if (mnemonic is not "sread" and not "aread" and not "read_char")
        {
            Execute(instruction, state);
            return;
        }

        if (state.IsHalted)
        {
            throw new ZMachineExecutionException("Cannot execute an instruction after the machine has halted.");
        }

        state.SetProgramCounter(instruction.NextProgramCounter);
        try
        {
            var input = new ZMachineInputHandler(state, inputProvider, timedRoutine, redrawInput);
            if (mnemonic is "sread" or "aread")
            {
                var terminatingCharacter = await input.ReadLineAsync(
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state),
                    instruction.Operands.Length > 2 ? ReadValue(instruction.Operands[2], state) : (ushort)0,
                    instruction.Operands.Length > 3 ? ReadValue(instruction.Operands[3], state) : (ushort)0,
                    cancellationToken);
                if (terminatingCharacter is { } result && instruction.StoreVariable is { } storeVariable)
                {
                    state.WriteVariable(storeVariable, result);
                }

                return;
            }

            var character = await input.ReadCharacterAsync(
                ReadValue(instruction.Operands[0], state),
                instruction.Operands.Length > 1 ? ReadValue(instruction.Operands[1], state) : (ushort)0,
                instruction.Operands.Length > 2 ? ReadValue(instruction.Operands[2], state) : (ushort)0,
                cancellationToken);
            StoreResult(instruction, state, character);
        }
        catch
        {
            state.SetProgramCounter(instruction.ProgramCounter);
            throw;
        }
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

        if (_writeText is not null)
        {
            state.OutputStreams.ScreenWriter ??= _writeText;
        }

        state.SetProgramCounter(instruction.NextProgramCounter);
        switch (instruction.Definition.Mnemonic)
        {
            case "print":
                state.OutputStreams.WriteText(new ZMachineTextCodec(state.Memory).DecodePackedText(instruction.InlineText.AsSpan()));
                break;
            case "print_ret":
                state.OutputStreams.WriteText(new ZMachineTextCodec(state.Memory).DecodePackedText(instruction.InlineText.AsSpan()));
                state.OutputStreams.WriteText("\n");
                state.ReturnFromRoutine(1);
                break;
            case "new_line":
                state.OutputStreams.WriteZsciiCharacter(13, new ZMachineTextCodec(state.Memory).Zscii);
                break;
            case "print_addr":
                state.OutputStreams.WriteText(new ZMachineTextCodec(state.Memory)
                    .DecodeStringAtByteAddress(ReadValue(instruction.Operands[0], state)));
                break;
            case "print_paddr":
                state.OutputStreams.WriteText(new ZMachineTextCodec(state.Memory)
                    .DecodePackedString(ReadValue(instruction.Operands[0], state)));
                break;
            case "print_char":
                {
                    var character = ReadValue(instruction.Operands[0], state);
                    if (character > byte.MaxValue)
                    {
                        throw new ZMachineExecutionException($"ZSCII character {character} is outside the supported output range.");
                    }

                    state.OutputStreams.WriteZsciiCharacter((byte)character, new ZMachineTextCodec(state.Memory).Zscii);
                    break;
                }
            case "print_num":
                state.OutputStreams.WriteText(ToSigned(ReadValue(instruction.Operands[0], state))
                    .ToString(CultureInfo.InvariantCulture));
                break;
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
                if (state.OutputStreams.ScreenWriter is null
                    && state.OutputStreams.TranscriptWriter is null
                    && state.OutputStreams.CurrentMemoryTableAddress is null)
                {
                    state.SetProgramCounter(instruction.ProgramCounter);
                    throw new ZMachineExecutionException("print_obj requires a configured text output callback.");
                }

                state.OutputStreams.WriteText(new ZMachineObjectTable(state).GetShortName(ReadValue(instruction.Operands[0], state)));
                break;
            case "print_table":
                PrintTable(instruction, state);
                break;
            case "print_unicode":
                {
                    var codePoint = ReadValue(instruction.Operands[0], state);
                    if (!Rune.IsValid(codePoint)
                        || codePoint is <= 0x001F or >= 0x007F and <= 0x009F)
                    {
                        throw new ZMachineExecutionException($"Unicode code point U+{codePoint:X4} is invalid.");
                    }

                    state.OutputStreams.WriteText(new Rune(codePoint).ToString());
                    break;
                }
            case "output_stream":
                {
                    var selectedStream = unchecked((short)ReadValue(instruction.Operands[0], state));
                    var tableAddress = instruction.Operands.Length > 1
                        ? ReadValue(instruction.Operands[1], state)
                        : (ushort?)null;
                    var width = instruction.Operands.Length > 2
                        ? unchecked((short)ReadValue(instruction.Operands[2], state))
                        : (short?)null;
                    state.OutputStreams.SelectStream(selectedStream, tableAddress, width);
                    break;
                }
            case "buffer_mode":
                state.OutputStreams.SetBuffering(ReadValue(instruction.Operands[0], state) == 1);
                break;
            case "split_window":
                state.OutputStreams.Screen.SplitWindow(ReadValue(instruction.Operands[0], state));
                break;
            case "set_window":
                state.OutputStreams.Screen.SetWindow(unchecked((short)ReadValue(instruction.Operands[0], state)));
                break;
            case "erase_window":
                state.OutputStreams.Screen.EraseWindow(unchecked((short)ReadValue(instruction.Operands[0], state)));
                break;
            case "erase_line":
                state.OutputStreams.Screen.EraseLine(ReadValue(instruction.Operands[0], state));
                break;
            case "set_cursor":
                if (state.Version == 6)
                {
                    state.OutputStreams.Screen.SetV6Cursor(
                        ReadValue(instruction.Operands[0], state),
                        instruction.Operands.Length > 1 ? ReadValue(instruction.Operands[1], state) : (ushort)0,
                        instruction.Operands.Length > 2
                            ? unchecked((short)ReadValue(instruction.Operands[2], state))
                            : (short)-3);
                }
                else
                {
                    state.OutputStreams.Screen.SetCursor(
                        ReadValue(instruction.Operands[0], state),
                        ReadValue(instruction.Operands[1], state));
                }

                break;
            case "get_cursor":
                state.OutputStreams.Screen.StoreCursorPosition(ReadValue(instruction.Operands[0], state));
                break;
            case "set_text_style":
                state.OutputStreams.Screen.SetTextStyle(ReadValue(instruction.Operands[0], state));
                break;
            case "set_font":
                StoreResult(instruction, state, state.OutputStreams.Screen.SetFont(
                    ReadValue(instruction.Operands[0], state),
                    instruction.Operands.Length > 1
                        ? unchecked((short)ReadValue(instruction.Operands[1], state))
                        : null));
                break;
            case "set_colour":
                if (state.Version == 6)
                {
                    state.OutputStreams.Screen.SetColours(
                        ReadValue(instruction.Operands[0], state),
                        ReadValue(instruction.Operands[1], state),
                        instruction.Operands.Length > 2
                            ? unchecked((short)ReadValue(instruction.Operands[2], state))
                            : (short)-3);
                }
                else
                {
                    state.OutputStreams.Screen.SetColours(
                        ReadValue(instruction.Operands[0], state),
                        ReadValue(instruction.Operands[1], state));
                }

                break;
            case "set_true_colour":
                state.OutputStreams.Screen.SetTrueColours(
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state),
                    instruction.Operands.Length > 2
                        ? unchecked((short)ReadValue(instruction.Operands[2], state))
                        : null);
                break;
            case "set_margins":
                state.OutputStreams.Screen.SetMargins(
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state),
                    unchecked((short)ReadValue(instruction.Operands[2], state)));
                break;
            case "draw_picture":
                ExecutePicture(instruction, state, erase: false);
                break;
            case "erase_picture":
                ExecutePicture(instruction, state, erase: true);
                break;
            case "picture_data":
                ExecutePictureData(instruction, state);
                break;
            case "picture_table":
                // Picture caching is optional; a host may load resources lazily when drawn.
                break;
            case "move_window":
                state.OutputStreams.Screen.MoveWindow(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    ReadValue(instruction.Operands[1], state),
                    ReadValue(instruction.Operands[2], state));
                break;
            case "window_size":
                state.OutputStreams.Screen.ResizeWindow(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    ReadValue(instruction.Operands[1], state),
                    ReadValue(instruction.Operands[2], state));
                break;
            case "window_style":
                state.OutputStreams.Screen.ChangeWindowStyle(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    ReadValue(instruction.Operands[1], state),
                    instruction.Operands.Length > 2 ? ReadValue(instruction.Operands[2], state) : (ushort)0);
                break;
            case "get_wind_prop":
                StoreResult(instruction, state, state.OutputStreams.Screen.GetWindowProperty(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    ReadValue(instruction.Operands[1], state)));
                break;
            case "scroll_window":
                state.OutputStreams.Screen.ScrollWindow(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    unchecked((short)ReadValue(instruction.Operands[1], state)));
                break;
            case "put_wind_prop":
                state.OutputStreams.Screen.PutWindowProperty(
                    unchecked((short)ReadValue(instruction.Operands[0], state)),
                    ReadValue(instruction.Operands[1], state),
                    ReadValue(instruction.Operands[2], state));
                break;
            case "buffer_screen":
                StoreResult(instruction, state, state.OutputStreams.Screen.BufferScreen(ReadValue(instruction.Operands[0], state)));
                break;
            case "read_mouse":
                ReadMouse(state, ReadValue(instruction.Operands[0], state));
                break;
            case "mouse_window":
                state.SetMouseWindow(unchecked((short)ReadValue(instruction.Operands[0], state)));
                break;
            case "make_menu":
                Branch(instruction, state, MakeMenu(
                    state,
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state)));
                break;
            case "sound_effect":
                ExecuteSoundEffect(instruction, state);
                break;
            case "show_status":
                state.OutputStreams.Screen.ShowStatusLine(state);
                break;
            case "input_stream":
                state.SelectInputStream(ReadValue(instruction.Operands[0], state));
                break;
            case "sread":
            case "aread":
            case "read_char":
                state.SetProgramCounter(instruction.ProgramCounter);
                throw new ZMachineExecutionException(
                    $"Opcode {instruction.Definition.Mnemonic} requires an input provider and must run through ExecuteAsync.");
            case "tokenise":
                new ZMachineDictionary(state.Memory).Tokenise(
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state),
                    instruction.Operands.Length > 2 ? ReadValue(instruction.Operands[2], state) : (ushort)0,
                    instruction.Operands.Length > 3 && ReadValue(instruction.Operands[3], state) != 0);
                break;
            case "encode_text":
                new ZMachineDictionary(state.Memory).EncodeText(
                    ReadValue(instruction.Operands[0], state),
                    ReadValue(instruction.Operands[1], state),
                    ReadValue(instruction.Operands[2], state),
                    ReadValue(instruction.Operands[3], state));
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
            case "save":
                ExecuteSave(instruction, state);
                break;
            case "restore":
                ExecuteRestore(instruction, state);
                break;
            case "restart":
                state.Restart();
                break;
            case "save_undo":
                StoreResult(instruction, state, state.SaveUndo(instruction.NextProgramCounter - 1) ? (ushort)1 : ushort.MaxValue);
                break;
            case "restore_undo":
                if (state.RestoreUndo())
                {
                    ExecuteSavedStore(state, 2);
                }
                else
                {
                    StoreResult(instruction, state, ushort.MaxValue);
                }
                break;
            default:
                state.SetProgramCounter(instruction.ProgramCounter);
                throw new ZMachineOpcodeNotImplementedException(
                    instruction.ProgramCounter,
                    instruction.Definition.Mnemonic,
                    $"{instruction.Definition.Mnemonic} is outside the arithmetic and execution operations implemented in Task 7.");
        }
    }

    private static void ExecuteSave(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var options = ReadSaveOptions(instruction, state);
        var savePc = state.Version <= 3
            ? instruction.Branch?.DescriptorAddress
                ?? throw new ZMachineExecutionException("A legacy save instruction has no branch descriptor address.")
            : instruction.NextProgramCounter - 1;
        var succeeded = state.SaveGame(options, savePc);
        if (state.Version <= 3)
        {
            Branch(instruction, state, succeeded);
        }
        else
        {
            StoreResult(instruction, state, succeeded ? (ushort)1 : (ushort)0);
        }
    }

    private static void ExecuteRestore(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var options = ReadSaveOptions(instruction, state);
        var succeeded = state.RestoreGame(options);
        if (state.Version <= 3)
        {
            if (succeeded)
            {
                ExecuteSavedBranch(state, true);
            }
            else
            {
                Branch(instruction, state, false);
            }
        }
        else
        {
            if (succeeded)
            {
                ExecuteSavedStore(state, options.ByteCount ?? (ushort)2);
            }
            else
            {
                StoreResult(instruction, state, 0);
            }
        }
    }

    private static void ExecuteSavedStore(ZMachineExecutionState state, ushort value)
    {
        var storeAddress = state.ProgramCounter;
        var variable = state.Memory.ReadStoryByte(storeAddress);
        state.SetProgramCounter(storeAddress + 1);
        state.WriteVariable(variable, value);
    }

    private static void ExecuteSavedBranch(ZMachineExecutionState state, bool condition)
    {
        var branchAddress = state.ProgramCounter;
        var first = state.Memory.ReadStoryByte(branchAddress);
        var branchOnTrue = (first & 0x80) != 0;
        short offset;
        uint afterDescriptor;
        if ((first & 0x40) != 0)
        {
            offset = (short)(first & 0x3F);
            afterDescriptor = branchAddress + 1;
        }
        else
        {
            var second = state.Memory.ReadStoryByte(branchAddress + 1);
            var encoded = (ushort)(((first & 0x3F) << 8) | second);
            offset = (short)((encoded & 0x2000) != 0 ? encoded | 0xC000 : encoded);
            afterDescriptor = branchAddress + 2;
        }

        state.SetProgramCounter(afterDescriptor);
        if (condition != branchOnTrue)
        {
            return;
        }

        if (offset is 0 or 1)
        {
            state.ReturnFromRoutine(offset == 1 ? (ushort)1 : (ushort)0);
        }
        else
        {
            state.SetProgramCounter(checked((uint)((long)afterDescriptor + offset - 2)));
        }
    }

    private static ZMachineSaveOptions ReadSaveOptions(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        if (instruction.Operands.IsEmpty)
        {
            return new ZMachineSaveOptions();
        }

        var values = instruction.Operands.Select(operand => ReadValue(operand, state)).ToArray();
        string? suggestedName = null;
        if (values.Length > 2 && values[2] != 0)
        {
            var length = state.Memory.ReadByte(values[2]);
            if ((uint)values[2] + length + 1 > state.Memory.Length)
            {
                throw new ZMachineExecutionException("The suggested save filename lies outside the story image.");
            }

            var chars = new char[length];
            for (var index = 0; index < length; index++)
            {
                var value = state.Memory.ReadByte(checked((ushort)(values[2] + index + 1)));
                chars[index] = value is >= 0x20 and <= 0x7E ? (char)value : '?';
            }

            suggestedName = new string(chars);
        }

        return new ZMachineSaveOptions(
            values.Length > 0 ? values[0] : null,
            values.Length > 1 ? values[1] : null,
            suggestedName,
            values.Length > 3 ? values[3] != 0 : null);
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

    private static void ReadMouse(ZMachineExecutionState state, ushort arrayAddress)
    {
        var mouse = state.EventServices.SupportsMouse
            ? state.EventServices.ReadMouse()
            : ZMachineMouseState.Offscreen;
        state.Memory.ValidateWritableRange(arrayAddress, 4 * sizeof(ushort));
        state.Memory.WriteWord(arrayAddress, unchecked((ushort)mouse.Y));
        state.Memory.WriteWord(checked((ushort)(arrayAddress + sizeof(ushort))), unchecked((ushort)mouse.X));
        state.Memory.WriteWord(checked((ushort)(arrayAddress + 2 * sizeof(ushort))), mouse.ButtonBits);
        state.Memory.WriteWord(checked((ushort)(arrayAddress + 3 * sizeof(ushort))), mouse.MenuSelection);
    }

    private static bool MakeMenu(ZMachineExecutionState state, ushort menuNumber, ushort tableAddress)
    {
        if (menuNumber < 3)
        {
            return false;
        }

        if (tableAddress == 0)
        {
            return state.EventServices.SupportsMenus
                && state.EventServices.MakeMenu(menuNumber, ImmutableArray<string>.Empty);
        }

        if (!state.EventServices.SupportsMenus)
        {
            return false;
        }

        state.Memory.ValidateReadableRange(tableAddress, sizeof(ushort));
        var itemCount = state.Memory.ReadWord(tableAddress);
        state.Memory.ValidateReadableRange((uint)tableAddress + sizeof(ushort), checked(itemCount * sizeof(ushort)));
        var codec = new ZsciiCodec(state.Memory);
        var entries = ImmutableArray.CreateBuilder<string>(itemCount);
        for (var index = 0; index < itemCount; index++)
        {
            var pointerAddress = checked((ushort)(tableAddress + sizeof(ushort) + index * sizeof(ushort)));
            var stringAddress = state.Memory.ReadWord(pointerAddress);
            entries.Add(ReadZsciiString(state.Memory, codec, stringAddress));
        }

        return state.EventServices.MakeMenu(menuNumber, entries.ToImmutable());
    }

    private static string ReadZsciiString(ZMachineMemory memory, ZsciiCodec codec, ushort address)
    {
        var characters = new StringBuilder();
        var cursor = (uint)address;
        for (var count = 0; count < 256; count++, cursor++)
        {
            memory.ValidateReadableRange(cursor, 1);
            var character = memory.ReadByte(checked((ushort)cursor));
            if (character == 0)
            {
                return characters.ToString();
            }

            var decoded = codec.Decode(character);
            if (decoded == '\uFFFD' || decoded == '\0')
            {
                throw new ZMachineExecutionException($"Menu string at 0x{address:X4} contains undefined ZSCII character {character}.");
            }

            characters.Append(decoded);
        }

        throw new ZMachineExecutionException($"Menu string at 0x{address:X4} is not terminated within 256 bytes.");
    }

    private static void ExecuteSoundEffect(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var values = instruction.Operands.Select(operand => ReadValue(operand, state)).ToArray();
        var number = values[0];
        if (values.Length == 1)
        {
            if (number is not (1 or 2))
            {
                throw new ZMachineExecutionException("A one-operand sound_effect must request bleep 1 or 2.");
            }

            state.EventServices.PerformSoundEffect(
                CreateSoundRequest(state, number, ZMachineSoundAction.Start, byte.MaxValue, 1, 0), null);
            return;
        }

        var actionValue = values[1];
        if (actionValue is < 1 or > 4)
        {
            throw new ZMachineExecutionException($"Sound effect action {actionValue} is not defined.");
        }

        if (number is 1 or 2 || (number == 0 && actionValue is not (3 or 4)))
        {
            throw new ZMachineExecutionException("Sound effects 1 and 2 are bleeps and do not accept effect operands.");
        }

        var volumeAndRepeats = values.Length > 2
            ? values[2]
            : state.Version == 3 ? (ushort)0x0008 : (ushort)0x0108;
        var volume = (byte)volumeAndRepeats;
        var repeats = (byte)(volumeAndRepeats >> 8);
        if (state.Version == 3 && repeats != 0)
        {
            throw new ZMachineExecutionException("Version 3 sound effects do not support repeat counts.");
        }

        if (state.Version >= 5 && actionValue == (ushort)ZMachineSoundAction.Start && repeats == 0)
        {
            repeats = 1;
        }

        var routine = state.Version >= 5 && values.Length > 3 ? values[3] : (ushort)0;
        Action? completed = routine == 0 || actionValue != (ushort)ZMachineSoundAction.Start
            ? null
            : () => state.RoutineCallRequested?.Invoke(routine);
        state.EventServices.PerformSoundEffect(
            CreateSoundRequest(state, number, (ZMachineSoundAction)actionValue, volume, repeats, routine), completed);
    }

    private static ZMachineSoundEffectRequest CreateSoundRequest(
        ZMachineExecutionState state,
        ushort number,
        ZMachineSoundAction action,
        byte volume,
        byte repeats,
        ushort routine)
    {
        BlorbSoundResource? resource = null;
        if (state.SoundCatalog is not null)
        {
            _ = state.SoundCatalog.TryGetSound(number, out resource);
        }

        var channel = resource?.Channel;
        var loopsForever = state.Version == 3
            ? resource is not null && !state.SoundCatalog!.PlaysOnceInVersionThree(number)
            : state.Version >= 5 && repeats == byte.MaxValue;
        return new ZMachineSoundEffectRequest(number, action, volume, repeats, routine, resource, channel, loopsForever);
    }

    private static void ExecutePicture(DecodedInstruction instruction, ZMachineExecutionState state, bool erase)
    {
        var number = ReadValue(instruction.Operands[0], state);
        var catalog = state.PictureCatalog;
        if (state.Version != 6 || catalog is null
            || !catalog.TryGetPicture(number, state.OutputStreams.Screen.WidthInUnits, state.OutputStreams.Screen.HeightInUnits, out _))
        {
            throw new ZMachineExecutionException($"Picture {number} is unavailable for {instruction.Definition.Mnemonic}.");
        }

        if (!erase)
        {
            _ = catalog.RecordPicturePlotted(number);
        }

        if (!catalog.TryGetPicture(
            number,
            state.OutputStreams.Screen.WidthInUnits,
            state.OutputStreams.Screen.HeightInUnits,
            out var picture)
            || picture is null)
        {
            throw new ZMachineExecutionException($"Picture {number} disappeared while it was being rendered.");
        }

        state.OutputStreams.Screen.DrawPicture(
            picture,
            ReadValue(instruction.Operands[1], state),
            ReadValue(instruction.Operands[2], state),
            erase);
    }

    private static void ExecutePictureData(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var number = ReadValue(instruction.Operands[0], state);
        var arrayAddress = ReadValue(instruction.Operands[1], state);
        var catalog = state.PictureCatalog;
        if (number == 0)
        {
            var count = catalog?.Count ?? 0;
            var releaseNumber = catalog?.ReleaseNumber ?? (ushort)0;
            WritePictureDataWords(
                state,
                arrayAddress,
                checked((ushort)Math.Min(count, ushort.MaxValue)),
                releaseNumber);
            Branch(instruction, state, count > 0);
            return;
        }

        if (catalog is null || !catalog.TryGetPicture(
            number,
            state.OutputStreams.Screen.WidthInUnits,
            state.OutputStreams.Screen.HeightInUnits,
            out var picture)
            || picture is null)
        {
            Branch(instruction, state, condition: false);
            return;
        }

        WritePictureDataWords(
            state,
            arrayAddress,
            checked((ushort)Math.Min(picture.DisplayHeight, ushort.MaxValue)),
            checked((ushort)Math.Min(picture.DisplayWidth, ushort.MaxValue)));
        Branch(instruction, state, condition: true);
    }

    private static void WritePictureDataWords(ZMachineExecutionState state, ushort address, ushort first, ushort second)
    {
        state.Memory.ValidateWritableRange(address, 2 * sizeof(ushort));
        state.Memory.WriteWord(address, first);
        state.Memory.WriteWord(checked((ushort)(address + sizeof(ushort))), second);
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

    private static void PrintTable(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        var textAddress = ReadValue(instruction.Operands[0], state);
        var width = ReadValue(instruction.Operands[1], state);
        var height = instruction.Operands.Length > 2 ? ReadValue(instruction.Operands[2], state) : (ushort)1;
        var skip = instruction.Operands.Length > 3 ? ReadValue(instruction.Operands[3], state) : (ushort)0;
        var totalLength = height == 0 || width == 0
            ? 0L
            : (long)height * width + ((long)height - 1) * skip;
        if (totalLength > int.MaxValue)
        {
            throw new ZMachineExecutionException("print_table range exceeds the supported story address range.");
        }

        state.Memory.ValidateReadableRange(textAddress, (int)totalLength);
        var zscii = new ZMachineTextCodec(state.Memory).Zscii;
        for (var row = 0; row < height; row++)
        {
            var rowAddress = checked((ushort)(textAddress + (row * (uint)(width + skip))));
            for (var column = 0; column < width; column++)
            {
                state.OutputStreams.WriteZsciiCharacter(
                    state.Memory.ReadByte(checked((ushort)(rowAddress + column))), zscii);
            }

            if (row + 1 < height)
            {
                state.OutputStreams.WriteZsciiCharacter(13, zscii);
            }
        }
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
