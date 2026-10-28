namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>The private interpreter state required to execute routines and variables.</summary>
public sealed class ZMachineExecutionState
{
    public const int DefaultMaximumStackWords = 32_768;
    private const int RoutineStackFrameOverhead = 4;

    private readonly List<ushort> _evaluationStack = [];
    private readonly List<ExecutionFrame> _callStack = [];
    private readonly int _maximumStackWords;

    private ZMachineExecutionState(ZMachineMemory memory, int maximumStackWords)
    {
        Memory = memory;
        _maximumStackWords = maximumStackWords;
    }

    public ZMachineMemory Memory { get; }

    public int MaximumStackWords => _maximumStackWords;

    public byte Version => Memory.Header.Version;

    public uint ProgramCounter { get; private set; }

    public ImmutableArray<ushort> EvaluationStack => _evaluationStack.ToImmutableArray();

    public ImmutableArray<ExecutionFrameSnapshot> CallStack =>
        _callStack.Select((frame, index) => frame.Snapshot(checked((ushort)index))).ToImmutableArray();

    public ExecutionFrameSnapshot CurrentFrame =>
        _callStack[^1].Snapshot(checked((ushort)(_callStack.Count - 1)));

    public byte CurrentArgumentCount => _callStack[^1].ArgumentCount;

    public static ZMachineExecutionState Start(StoryFile story, int maximumStackWords = DefaultMaximumStackWords)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (maximumStackWords < 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumStackWords), maximumStackWords,
                "The Z-machine stack must support at least 1,024 words.");
        }

        var state = new ZMachineExecutionState(new ZMachineMemory(story), maximumStackWords);
        if (story.Header.Version == 6)
        {
            var routineAddress = ZMachineAddressing.InitialProgramCounterByteAddress(story.Header);
            var main = state.ReadRoutineFrame(routineAddress, [], null, null, isRoot: true);
            state.EnsureStackSpace(RoutineStackFrameOverhead + main.Locals.Length);
            state._callStack.Add(main);
            state.ProgramCounter = main.FirstInstructionAddress;
        }
        else
        {
            state._callStack.Add(new ExecutionFrame(null, null, null, [], 0, 0, isRoot: true, story.Header.InitialProgramCounter));
            state.ProgramCounter = story.Header.InitialProgramCounter;
        }

        return state;
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
    public ZMachineExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
