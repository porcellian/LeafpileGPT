namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;

/// <summary>The private interpreter state required to execute routines and variables.</summary>
public sealed class ZMachineExecutionState
{
    public const int DefaultMaximumStackWords = 32_768;
    private const int RoutineStackFrameOverhead = 4;

    private readonly List<ushort> _evaluationStack = [];
    private readonly List<ExecutionFrame> _callStack = [];
    private readonly int _maximumStackWords;
    private readonly StoryFile _story;
    private readonly int? _deterministicRandomSeed;
    private Random? _deterministicRandom;
    private QuetzalSaveData? _undoSave;
    private bool _isPredictableRandomMode;
    private int _predictableSeed;
    private int _nextPredictableNumber;
    private uint _largePredictableState;

    private ZMachineExecutionState(
        ZMachineMemory memory,
        StoryFile story,
        int maximumStackWords,
        IZMachineEventServices eventServices,
        BlorbPictureCatalog? pictureCatalog,
        BlorbSoundCatalog? soundCatalog,
        int? deterministicRandomSeed)
    {
        Memory = memory;
        _story = story;
        _maximumStackWords = maximumStackWords;
        EventServices = eventServices;
        PictureCatalog = pictureCatalog;
        SoundCatalog = soundCatalog;
        _deterministicRandomSeed = deterministicRandomSeed;
        OutputStreams = new ZMachineOutputStreams(memory);
        OutputStreams.Screen.NewlineInterruptHandler = routine => RoutineCallRequested?.Invoke(routine);
        InitializePictureCapabilities();
        InitializeEventCapabilities();
    }

    public ZMachineMemory Memory { get; }

    public ZMachineOutputStreams OutputStreams { get; }

    /// <summary>Host services for optional mouse, V6 menu, and sound features.</summary>
    public IZMachineEventServices EventServices { get; }

    /// <summary>Optional host storage for Save and Restore opcodes.</summary>
    public IZMachineSaveStore? SaveStore { get; set; }

    /// <summary>Optional Blorb picture resources and display hints for this game session.</summary>
    public BlorbPictureCatalog? PictureCatalog { get; }

    /// <summary>Optional Blorb sound resources and V3 loop metadata for this game session.</summary>
    public BlorbSoundCatalog? SoundCatalog { get; }

    /// <summary>Host callback used to execute a game routine requested by a VM event.</summary>
    public Action<ushort>? RoutineCallRequested { get; set; }

    public int MaximumStackWords => _maximumStackWords;

    public bool IsPredictableRandomMode => _isPredictableRandomMode;

    public byte Version => Memory.Header.Version;

    public byte CurrentInputStream { get; private set; }

    public short MouseWindow { get; private set; } = 1;

    public uint ProgramCounter { get; private set; }

    public bool IsHalted { get; private set; }

    /// <summary>True when the last opcode restore resumed a host checkpoint at an input instruction.</summary>
    public bool LastRestoreWasHostCheckpoint { get; private set; }

    public ImmutableArray<ushort> EvaluationStack => _evaluationStack.ToImmutableArray();

    /// <summary>Returns an immutable copy of the current runtime header for diagnostic views.</summary>
    public ImmutableArray<byte> CaptureRuntimeHeaderSnapshot() => Memory.CaptureHeaderSnapshot();

    public ImmutableArray<ExecutionFrameSnapshot> CallStack =>
        _callStack.Select((frame, index) => frame.Snapshot(checked((ushort)index))).ToImmutableArray();

    public ExecutionFrameSnapshot CurrentFrame =>
        _callStack[^1].Snapshot(checked((ushort)(_callStack.Count - 1)));

    public byte CurrentArgumentCount => _callStack[^1].ArgumentCount;

    /// <summary>Implements the random opcode's random, seeded, and random-mode reset behavior.</summary>
    public ushort GetRandomNumber(short range)
    {
        if (range == 0)
        {
            _isPredictableRandomMode = false;
            return 0;
        }

        if (range < 0)
        {
            _predictableSeed = Math.Abs((int)range);
            _nextPredictableNumber = 1;
            _largePredictableState = (uint)_predictableSeed;
            _isPredictableRandomMode = true;
            return 0;
        }

        if (!_isPredictableRandomMode)
        {
            return (ushort)(_deterministicRandom?.Next(1, range + 1)
                ?? RandomNumberGenerator.GetInt32(1, range + 1));
        }

        if (_predictableSeed < 1_000)
        {
            var next = _nextPredictableNumber;
            _nextPredictableNumber = next == _predictableSeed ? 1 : next + 1;
            return (ushort)(((next - 1) % range) + 1);
        }

        _largePredictableState ^= _largePredictableState << 13;
        _largePredictableState ^= _largePredictableState >> 17;
        _largePredictableState ^= _largePredictableState << 5;
        return (ushort)((_largePredictableState % (uint)range) + 1);
    }

    public void Halt() => IsHalted = true;

    /// <summary>Records a host mouse click in header extension words 1 (y) and 2 (x), when present.</summary>
    public void RecordMouseClick(short y, short x)
    {
        if (Version < 5 || Memory.Header.HeaderExtensionTableAddress is not { } extensionAddress || extensionAddress == 0)
        {
            return;
        }

        Memory.ValidateReadableRange(extensionAddress, sizeof(ushort));
        var wordCount = Memory.ReadWord(extensionAddress);
        if (wordCount < 2)
        {
            return;
        }

        var firstWord = (uint)extensionAddress + sizeof(ushort);
        Memory.ValidateWritableRange(firstWord, 2 * sizeof(ushort));
        Memory.WriteWord(checked((ushort)firstWord), unchecked((ushort)y));
        Memory.WriteWord(checked((ushort)(firstWord + sizeof(ushort))), unchecked((ushort)x));
    }

    /// <summary>Constrains V6 mouse input to a window, or frees it when set to -1.</summary>
    public void SetMouseWindow(short window)
    {
        if (Version != 6)
        {
            throw new ZMachineExecutionException("Mouse windows are available only in Version 6.");
        }

        if (window is < -1 or > 7)
        {
            throw new ZMachineExecutionException($"Mouse window {window} is outside the supported range -1 through 7.");
        }

        MouseWindow = window;
        EventServices.SetMouseWindow(window);
    }

    public void SelectInputStream(ushort inputStream)
    {
        if (Version < 3 || inputStream > 1)
        {
            throw new ZMachineExecutionException(
                $"Input stream {inputStream} is not defined for Z-machine version {Version}.");
        }

        CurrentInputStream = checked((byte)inputStream);
        OutputStreams.Screen.SetMorePromptsEnabled(inputStream == 0);
    }

    /// <summary>Sets the interpreter capability bit for periodic timed input callbacks.</summary>
    public void SetTimedInputSupported(bool supported)
    {
        if (Version < 4)
        {
            return;
        }

        var flags1 = Memory.ReadByte(0x01);
        Memory.WriteByte(0x01, supported ? (byte)(flags1 | 0x80) : (byte)(flags1 & 0x7F));
    }

    public static ZMachineExecutionState Start(
        StoryFile story,
        int maximumStackWords = DefaultMaximumStackWords,
        IZMachineEventServices? eventServices = null,
        BlorbPictureCatalog? pictureCatalog = null,
        BlorbSoundCatalog? soundCatalog = null,
        IZMachineSaveStore? saveStore = null,
        int? deterministicRandomSeed = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (maximumStackWords < 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumStackWords), maximumStackWords,
                "The Z-machine stack must support at least 1,024 words.");
        }

        var state = new ZMachineExecutionState(
            new ZMachineMemory(story), story, maximumStackWords, eventServices ?? NullZMachineEventServices.Instance,
            pictureCatalog, soundCatalog, deterministicRandomSeed);
        state.SaveStore = saveStore;
        state.InitializeExecutionPoint();
        return state;
    }

    private void InitializeExecutionPoint()
    {
        _deterministicRandom = _deterministicRandomSeed is { } seed ? new Random(seed) : null;
        _evaluationStack.Clear();
        _callStack.Clear();
        ProgramCounter = 0;
        IsHalted = false;
        CurrentInputStream = 0;
        MouseWindow = 1;
        if (Version == 6)
        {
            var routineAddress = ZMachineAddressing.InitialProgramCounterByteAddress(Memory.Header);
            var main = ReadRoutineFrame(routineAddress, [], null, null, isRoot: true);
            EnsureStackSpace(RoutineStackFrameOverhead + main.Locals.Length);
            _callStack.Add(main);
            ProgramCounter = main.FirstInstructionAddress;
        }
        else
        {
            _callStack.Add(new ExecutionFrame(null, null, null, [], 0, 0, isRoot: true, Memory.Header.InitialProgramCounter));
            ProgramCounter = Memory.Header.InitialProgramCounter;
        }
    }

    /// <summary>Captures the complete interpreter-owned state in the Quetzal data model.</summary>
    public QuetzalSaveData CaptureSaveState(uint? programCounter = null)
    {
        var pc = programCounter ?? ProgramCounter;
        var frames = ImmutableArray.CreateBuilder<QuetzalStackFrame>();
        if (Version != 6)
        {
            var end = _callStack.Count > 1 ? _callStack[1].EvaluationStackBase : _evaluationStack.Count;
            frames.Add(new QuetzalStackFrame(0, false, 0, 0, [],
                _evaluationStack.Take(end).ToImmutableArray(), IsDummy: true));
        }

        var firstCallFrame = Version == 6 ? 0 : 1;
        for (var index = firstCallFrame; index < _callStack.Count; index++)
        {
            var frame = _callStack[index];
            var stackEnd = index + 1 < _callStack.Count
                ? _callStack[index + 1].EvaluationStackBase
                : _evaluationStack.Count;
            var argumentMask = frame.ArgumentCount == 0 ? (byte)0 : (byte)((1 << frame.ArgumentCount) - 1);
            frames.Add(new QuetzalStackFrame(
                frame.ReturnProgramCounter ?? 0,
                frame.StoreVariable is null,
                frame.StoreVariable ?? 0,
                argumentMask,
                frame.Locals.ToImmutableArray(),
                _evaluationStack.Skip(frame.EvaluationStackBase).Take(stackEnd - frame.EvaluationStackBase).ToImmutableArray()));
        }

        return new QuetzalSaveData(Version, QuetzalStoryIdentity.FromStory(_story, pc),
            Memory.CaptureDynamicMemory().ToImmutableArray(), frames.ToImmutable(), ImmutableArray<QuetzalChunk>.Empty);
    }

    /// <summary>Attempts to write the current state as a Quetzal IFZS image.</summary>
    public bool SaveGame(ZMachineSaveOptions? options = null, uint? programCounter = null)
    {
        if (SaveStore is null)
        {
            return false;
        }

        var save = CaptureSaveState(programCounter);
        return SaveStore.Save(QuetzalCodec.Encode(save, _story), options ?? new ZMachineSaveOptions());
    }

    /// <summary>Loads, validates, and atomically applies a Quetzal state for this story.</summary>
    public bool RestoreGame(ZMachineSaveOptions? options = null)
    {
        LastRestoreWasHostCheckpoint = false;
        var image = SaveStore?.Restore(options ?? new ZMachineSaveOptions());
        if (image is null)
        {
            return false;
        }

        var save = QuetzalCodec.Decode(image, _story);
        var markedHostCheckpoint = QuetzalCodec.IsHostCheckpoint(save);
        ApplySaveState(save);
        LastRestoreWasHostCheckpoint = markedHostCheckpoint || IsLegacyInputCheckpoint(save.Identity.ProgramCounter);
        _undoSave = null;
        return true;
    }

    /// <summary>Stores a single in-memory undo point, excluding its predecessor.</summary>
    public bool SaveUndo(uint? programCounter = null)
    {
        _undoSave = CaptureSaveState(programCounter);
        return true;
    }

    /// <summary>Restores and consumes the most recent in-memory undo point.</summary>
    public bool RestoreUndo()
    {
        if (_undoSave is null)
        {
            return false;
        }

        var save = _undoSave;
        _undoSave = null;
        ApplySaveState(save);
        return true;
    }

    /// <summary>Applies a validated snapshot while preserving interpreter-owned header state.</summary>
    public void ApplySaveState(QuetzalSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);
        LastRestoreWasHostCheckpoint = false;
        // Encoding validates every field before live state is touched.
        _ = QuetzalCodec.Encode(save, _story);
        var preservedFlags2 = Memory.ReadWord(0x10);
        var resetHeader = CaptureResetHeader();
        var frames = RebuildFrames(save.StackFrames, save.Identity.ProgramCounter);
        var values = save.StackFrames.SelectMany(frame => frame.EvaluationStack).ToList();
        var frameWords = frames.Sum(frame => RoutineStackFrameOverhead + frame.Locals.Length);
        if (frameWords + values.Count > _maximumStackWords)
        {
            throw new QuetzalFormatException($"Restored VM stack exceeds the configured {_maximumStackWords}-word limit.");
        }

        Memory.RestoreDynamicMemory(save.DynamicMemory.AsSpan());
        Memory.WriteWord(0x10, preservedFlags2);
        ApplyResetHeader(resetHeader);
        _evaluationStack.Clear();
        _evaluationStack.AddRange(values);
        _callStack.Clear();
        _callStack.AddRange(frames);
        ProgramCounter = save.Identity.ProgramCounter;
        IsHalted = false;
        CurrentInputStream = 0;
        MouseWindow = 1;
        OutputStreams.ResetAfterRestore();
        if (Version == 6)
        {
            EventServices.SetMouseWindow(MouseWindow);
        }
    }

    /// <summary>Restarts from the original story image while retaining Flags 2 and host header capabilities.</summary>
    public void Restart()
    {
        LastRestoreWasHostCheckpoint = false;
        var preservedFlags2 = Memory.ReadWord(0x10);
        var resetHeader = CaptureResetHeader();
        Memory.RestoreOriginalStory();
        Memory.WriteWord(0x10, preservedFlags2);
        ApplyResetHeader(resetHeader);
        _undoSave = null;
        _isPredictableRandomMode = false;
        _predictableSeed = 0;
        _nextPredictableNumber = 1;
        _largePredictableState = 0;
        InitializeExecutionPoint();
        OutputStreams.ResetAfterRestore();
        if (Version == 6)
        {
            EventServices.SetMouseWindow(MouseWindow);
        }
    }

    private ImmutableArray<ExecutionFrame> RebuildFrames(ImmutableArray<QuetzalStackFrame> savedFrames, uint pc)
    {
        var frames = ImmutableArray.CreateBuilder<ExecutionFrame>(savedFrames.Length);
        var stackBase = 0;
        for (var index = 0; index < savedFrames.Length; index++)
        {
            var saved = savedFrames[index];
            var isDummy = saved.IsDummy;
            var argumentCount = checked((byte)BitOperations.PopCount(saved.ArgumentMask));
            var isRoot = isDummy || (index == 0 && Version == 6);
            frames.Add(new ExecutionFrame(null,
                isDummy || isRoot ? null : saved.ReturnProgramCounter,
                isDummy || isRoot || saved.DiscardsResult ? null : saved.StoreVariable,
                saved.Locals.ToArray(), argumentCount, stackBase, isRoot, pc));
            stackBase += saved.EvaluationStack.Length;
        }

        return frames.ToImmutable();
    }

    private byte[] CaptureResetHeader()
    {
        var values = new byte[13];
        values[0] = Memory.ReadByte(0x01);
        if (Version >= 4)
        {
            for (var address = 0x1E; address <= 0x27; address++)
            {
                values[address - 0x1D] = Memory.ReadByte((ushort)address);
            }
        }

        if (Version >= 5)
        {
            values[11] = Memory.ReadByte(0x2C);
            values[12] = Memory.ReadByte(0x2D);
        }
        return values;
    }

    private void ApplyResetHeader(ReadOnlySpan<byte> values)
    {
        Memory.WriteByte(0x01, values[0]);
        if (Version >= 4)
        {
            for (var address = 0x1E; address <= 0x27; address++)
            {
                Memory.WriteByte((ushort)address, values[address - 0x1D]);
            }
        }

        if (Version >= 5)
        {
            Memory.WriteByte(0x2C, values[11]);
            Memory.WriteByte(0x2D, values[12]);
        }
    }

    private bool IsLegacyInputCheckpoint(uint programCounter)
    {
        try
        {
            var instruction = new ZMachineInstructionDecoder(Memory).Decode(programCounter);
            return instruction.Definition.Mnemonic is "sread" or "aread" or "read_char";
        }
        catch (Exception exception) when (exception is ZMachineInstructionException or ZMachineExecutionException)
        {
            return false;
        }
    }

    private void InitializeEventCapabilities()
    {
        var flags2 = Memory.ReadWord(0x10);
        if (!EventServices.SupportsMouse)
        {
            flags2 = (ushort)(flags2 & ~0x0020);
        }

        if (!EventServices.SupportsMenus)
        {
            flags2 = (ushort)(flags2 & ~0x0100);
        }

        if (!EventServices.SupportsSoundEffects)
        {
            flags2 = (ushort)(flags2 & ~0x0080);
        }

        Memory.WriteWord(0x10, flags2);
        if (Version == 6)
        {
            var flags1 = Memory.ReadByte(0x01);
            Memory.WriteByte(0x01, EventServices.SupportsSoundEffects
                ? (byte)(flags1 | 0x20)
                : (byte)(flags1 & ~0x20));
        }
    }

    private void InitializePictureCapabilities()
    {
        if (Version != 6)
        {
            return;
        }

        const byte pictureDisplayAvailable = 0x02;
        var flags1 = Memory.ReadByte(0x01);
        flags1 = PictureCatalog is { Count: > 0 }
            ? (byte)(flags1 | pictureDisplayAvailable)
            : (byte)(flags1 & ~pictureDisplayAvailable);
        Memory.WriteByte(0x01, flags1);
    }

    public void SetProgramCounter(uint byteAddress)
    {
        if (byteAddress >= Memory.Length)
        {
            throw new ZMachineExecutionException($"Program counter 0x{byteAddress:X} lies outside the story image.");
        }

        ProgramCounter = byteAddress;
    }

    /// <summary>Reads a variable; variable 0 pops the top of the current evaluation stack.</summary>
    public ushort ReadVariable(byte variableNumber)
    {
        if (variableNumber == 0)
        {
            return PopEvaluationStack();
        }

        if (variableNumber <= 15)
        {
            var locals = _callStack[^1].Locals;
            if (variableNumber > locals.Length)
            {
                throw new ZMachineExecutionException(
                    $"Local variable {variableNumber} does not exist in the current routine.");
            }

            return locals[variableNumber - 1];
        }

        var address = (uint)Memory.Header.GlobalsTableAddress + ((uint)variableNumber - 16) * sizeof(ushort);
        return Memory.ReadWord(checked((ushort)address));
    }

    /// <summary>Writes a variable; variable 0 pushes the value on the current evaluation stack.</summary>
    public void WriteVariable(byte variableNumber, ushort value)
    {
        if (variableNumber == 0)
        {
            PushEvaluationStack(value);
            return;
        }

        if (variableNumber <= 15)
        {
            var locals = _callStack[^1].Locals;
            if (variableNumber > locals.Length)
            {
                throw new ZMachineExecutionException(
                    $"Local variable {variableNumber} does not exist in the current routine.");
            }

            locals[variableNumber - 1] = value;
            return;
        }

        var address = (uint)Memory.Header.GlobalsTableAddress + ((uint)variableNumber - 16) * sizeof(ushort);
        Memory.WriteWord(checked((ushort)address), value);
    }

    /// <summary>Reads an indirect variable reference; reference 0 peeks without popping.</summary>
    public ushort ReadVariableReference(byte variableNumber)
    {
        if (variableNumber != 0)
        {
            return ReadVariable(variableNumber);
        }

        EnsureEvaluationStackNotEmpty();
        return _evaluationStack[^1];
    }

    /// <summary>Writes an indirect variable reference; reference 0 replaces the top stack value.</summary>
    public void WriteVariableReference(byte variableNumber, ushort value)
    {
        if (variableNumber != 0)
        {
            WriteVariable(variableNumber, value);
            return;
        }

        EnsureEvaluationStackNotEmpty();
        _evaluationStack[^1] = value;
    }

    public void PushEvaluationStack(ushort value)
    {
        EnsureStackSpace(1);
        _evaluationStack.Add(value);
    }

    public ushort PopEvaluationStack()
    {
        EnsureEvaluationStackNotEmpty();
        var top = _evaluationStack[^1];
        _evaluationStack.RemoveAt(_evaluationStack.Count - 1);
        return top;
    }

    /// <summary>Calls a packed routine. A packed address of zero returns false without making a frame.</summary>
    public void CallRoutine(
        ushort packedRoutineAddress,
        ReadOnlySpan<ushort> arguments,
        uint returnProgramCounter,
        byte? storeVariable)
    {
        var maximumArguments = Version <= 3 ? 3 : 7;
        if (arguments.Length > maximumArguments)
        {
            throw new ZMachineExecutionException(
                $"Version {Version} routines accept at most {maximumArguments} arguments; received {arguments.Length}.");
        }

        ValidateProgramCounter(returnProgramCounter, nameof(returnProgramCounter));
        if (packedRoutineAddress == 0)
        {
            ProgramCounter = returnProgramCounter;
            if (storeVariable is not null)
            {
                WriteVariable(storeVariable.Value, 0);
            }

            return;
        }

        if (_callStack.Count >= ushort.MaxValue)
        {
            throw new ZMachineExecutionException("The call stack cannot represent another stack-frame index.");
        }

        var routineAddress = ZMachineAddressing.PackedToByteAddress(packedRoutineAddress, Memory.Header, PackedAddressKind.Routine);
        var frame = ReadRoutineFrame(routineAddress, arguments, storeVariable, returnProgramCounter, isRoot: false);
        EnsureStackSpace(RoutineStackFrameOverhead + frame.Locals.Length);
        _callStack.Add(frame);
        ProgramCounter = frame.FirstInstructionAddress;
    }

    public void ReturnFromRoutine(ushort returnValue)
    {
        var returningFrame = _callStack[^1];
        if (returningFrame.IsRoot)
        {
            throw new ZMachineExecutionException("Returning from the initial execution environment is illegal.");
        }

        RemoveFrameAndRestoreCaller(returningFrame, returnValue);
    }

    /// <summary>Returns the index of the current active routine frame for catch/throw.</summary>
    public ushort CatchFrame()
    {
        EnsureCatchThrowAvailability();
        return checked((ushort)(_callStack.Count - 1));
    }

    /// <summary>Unwinds through the caught routine and returns the supplied value to its caller.</summary>
    public void ThrowToFrame(ushort frameIndex, ushort returnValue)
    {
        EnsureCatchThrowAvailability();
        if (frameIndex == 0 || frameIndex >= _callStack.Count)
        {
            throw new ZMachineExecutionException($"Stack-frame index {frameIndex} does not identify a throwable routine frame.");
        }

        var throwingTarget = _callStack[frameIndex];
        if (throwingTarget.IsRoot)
        {
            throw new ZMachineExecutionException("The initial execution environment cannot be the target of throw.");
        }

        RemoveFrameAndRestoreCaller(throwingTarget, returnValue, frameIndex);
    }

    public bool HasArgument(byte oneBasedArgumentNumber)
    {
        if (oneBasedArgumentNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(oneBasedArgumentNumber), "Argument numbers start at 1.");
        }

        return oneBasedArgumentNumber <= CurrentArgumentCount;
    }

    /// <summary>Pushes to a V6 user stack, returning false without mutation when it is full.</summary>
    public bool PushUserStack(ushort value, ushort stackTableAddress)
    {
        EnsureVersionSixUserStack();
        var spareSlots = Memory.ReadWord(stackTableAddress);
        if (spareSlots == 0)
        {
            return false;
        }

        var targetAddress = GetUserStackWordAddress(stackTableAddress, spareSlots);
        Memory.WriteWord(targetAddress, value);
        Memory.WriteWord(stackTableAddress, (ushort)(spareSlots - 1));
        return true;
    }

    /// <summary>Pulls from the game stack when address is zero, otherwise from a V6 user stack.</summary>
    public ushort PullStack(ushort stackTableAddress)
    {
        if (stackTableAddress == 0)
        {
            return PopEvaluationStack();
        }

        EnsureVersionSixUserStack();
        var spareSlots = Memory.ReadWord(stackTableAddress);
        var newSpareSlots = unchecked((ushort)(spareSlots + 1));
        var value = Memory.ReadWord(GetUserStackWordAddress(stackTableAddress, newSpareSlots));
        Memory.WriteWord(stackTableAddress, newSpareSlots);
        return value;
    }

    /// <summary>Removes items from the game stack when address is zero, otherwise adjusts a V6 user stack.</summary>
    public void PopStack(ushort itemCount, ushort stackTableAddress)
    {
        if (stackTableAddress == 0)
        {
            if (itemCount > _evaluationStack.Count - CurrentFrame.EvaluationStackBase)
            {
                throw new ZMachineExecutionException("Cannot pop more values than are on the current routine's evaluation stack.");
            }

            _evaluationStack.RemoveRange(_evaluationStack.Count - itemCount, itemCount);
            return;
        }

        EnsureVersionSixUserStack();
        var spareSlots = Memory.ReadWord(stackTableAddress);
        Memory.WriteWord(stackTableAddress, unchecked((ushort)(spareSlots + itemCount)));
    }

    private ExecutionFrame ReadRoutineFrame(
        uint routineAddress,
        ReadOnlySpan<ushort> arguments,
        byte? storeVariable,
        uint? returnProgramCounter,
        bool isRoot)
    {
        byte localCount;
        try
        {
            localCount = Memory.ReadStoryByte(routineAddress);
        }
        catch (ZMachineMemoryAccessException exception)
        {
            throw new ZMachineExecutionException($"Routine header at 0x{routineAddress:X} lies outside the story image.", exception);
        }

        if (localCount > 15)
        {
            throw new ZMachineExecutionException($"Routine at 0x{routineAddress:X} declares {localCount} locals; the maximum is 15.");
        }

        var locals = new ushort[localCount];
        var firstInstructionAddress = routineAddress + 1;
        if (Version <= 4)
        {
            for (var localIndex = 0; localIndex < localCount; localIndex++)
            {
                try
                {
                    locals[localIndex] = Memory.ReadStoryWord(firstInstructionAddress);
                    firstInstructionAddress += sizeof(ushort);
                }
                catch (ZMachineMemoryAccessException exception)
                {
                    throw new ZMachineExecutionException(
                        $"Routine at 0x{routineAddress:X} has a truncated local-variable header.", exception);
                }
            }
        }

        for (var index = 0; index < Math.Min(locals.Length, arguments.Length); index++)
        {
            locals[index] = arguments[index];
        }

        ValidateProgramCounter(firstInstructionAddress, nameof(routineAddress));
        return new ExecutionFrame(routineAddress, returnProgramCounter, storeVariable, locals,
            checked((byte)arguments.Length), _evaluationStack.Count, isRoot, firstInstructionAddress);
    }

    private void RemoveFrameAndRestoreCaller(ExecutionFrame frame, ushort returnValue, int? frameIndex = null)
    {
        var index = frameIndex ?? (_callStack.Count - 1);
        if (index <= 0 || index >= _callStack.Count || !ReferenceEquals(_callStack[index], frame))
        {
            throw new ZMachineExecutionException("The routine frame is no longer active.");
        }

        _evaluationStack.RemoveRange(frame.EvaluationStackBase, _evaluationStack.Count - frame.EvaluationStackBase);
        _callStack.RemoveRange(index, _callStack.Count - index);
        ProgramCounter = frame.ReturnProgramCounter
            ?? throw new ZMachineExecutionException("A non-root routine frame has no return address.");
        if (frame.StoreVariable is not null)
        {
            WriteVariable(frame.StoreVariable.Value, returnValue);
        }
    }

    private ushort GetUserStackWordAddress(ushort tableAddress, ushort slotIndex)
    {
        var byteAddress = (uint)tableAddress + (uint)slotIndex * sizeof(ushort);
        if (byteAddress > ushort.MaxValue - (sizeof(ushort) - 1))
        {
            throw new ZMachineExecutionException($"User-stack word at 0x{byteAddress:X} exceeds game-visible memory.");
        }

        return (ushort)byteAddress;
    }

    private void EnsureVersionSixUserStack()
    {
        if (Version != 6)
        {
            throw new ZMachineExecutionException("User stacks are available only in Version 6.");
        }
    }

    private void EnsureCatchThrowAvailability()
    {
        if (Version < 5)
        {
            throw new ZMachineExecutionException("Catch and throw are available only in Version 5 and later.");
        }
    }

    private void EnsureEvaluationStackNotEmpty()
    {
        if (_evaluationStack.Count <= CurrentFrame.EvaluationStackBase)
        {
            throw new ZMachineExecutionException("The current routine's evaluation stack is empty.");
        }
    }

    private void EnsureStackSpace(int additionalWords)
    {
        var frameWords = _callStack.Sum(frame => RoutineStackFrameOverhead + frame.Locals.Length);
        if ((long)frameWords + _evaluationStack.Count + additionalWords > _maximumStackWords)
        {
            throw new ZMachineExecutionException($"The Z-machine stack exceeds its {_maximumStackWords}-word capacity.");
        }
    }

    private void ValidateProgramCounter(uint byteAddress, string parameterName)
    {
        if (byteAddress >= Memory.Length)
        {
            throw new ZMachineExecutionException($"Program counter 0x{byteAddress:X} from {parameterName} lies outside the story image.");
        }
    }

    private sealed class ExecutionFrame(
        uint? routineAddress,
        uint? returnProgramCounter,
        byte? storeVariable,
        ushort[] locals,
        byte argumentCount,
        int evaluationStackBase,
        bool isRoot,
        uint firstInstructionAddress)
    {
        public uint? RoutineAddress { get; } = routineAddress;

        public uint? ReturnProgramCounter { get; } = returnProgramCounter;

        public byte? StoreVariable { get; } = storeVariable;

        public ushort[] Locals { get; } = locals;

        public byte ArgumentCount { get; } = argumentCount;

        public int EvaluationStackBase { get; } = evaluationStackBase;

        public bool IsRoot { get; } = isRoot;

        public uint FirstInstructionAddress { get; } = firstInstructionAddress;

        public ExecutionFrameSnapshot Snapshot(ushort stackFrameIndex) => new(
            stackFrameIndex,
            RoutineAddress,
            ReturnProgramCounter,
            StoreVariable,
            Locals.ToImmutableArray(),
            ArgumentCount,
            EvaluationStackBase,
            IsRoot,
            FirstInstructionAddress);
    }
}

public sealed record ExecutionFrameSnapshot(
    ushort StackFrameIndex,
    uint? RoutineAddress,
    uint? ReturnProgramCounter,
    byte? StoreVariable,
    ImmutableArray<ushort> Locals,
    byte ArgumentCount,
    int EvaluationStackBase,
    bool IsRoot,
    uint FirstInstructionAddress);

public sealed class ZMachineExecutionException : Exception
{
    public ZMachineExecutionException(string message, Exception? innerException = null, bool isFatal = false)
        : base(message, innerException)
    {
        IsFatal = isFatal;
    }

    public bool IsFatal { get; }
}
