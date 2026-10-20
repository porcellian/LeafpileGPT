using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

public sealed class ZMachineCallStack
{
    public const int MaximumStackWords = 61440;

    private readonly ZMachineMemory _memory;
    private readonly StoryHeader _header;
    private readonly List<RoutineFrame> _frames = [];
    private readonly List<ushort> _evaluationStack = [];
    private int _routineUsage;
    private ushort _nextFrameToken = 1;

    public ZMachineCallStack(ZMachineMemory memory, StoryHeader header)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(header);

        _memory = memory;
        _header = header;

        if (header.Version == 6)
        {
            var mainRoutine = ReadRoutine(header.InitialProgramAddress, 0, null, argumentCount: 0, isRoot: true);
            _frames.Add(mainRoutine.Frame);
            _routineUsage = mainRoutine.Frame.StackUsage;
            ProgramCounter = mainRoutine.FirstInstructionAddress;
        }
        else
        {
            _frames.Add(RoutineFrame.CreateEnvironment());
            ProgramCounter = header.InitialProgramAddress;
        }
    }

    public int ProgramCounter { get; private set; }

    public int FrameCount => _frames.Count;

    public int EvaluationStackDepth => _evaluationStack.Count;

    public int CurrentRoutineAddress => CurrentFrame.RoutineAddress;

    public int CurrentArgumentCount => CurrentFrame.ArgumentCount;

    public ushort CurrentFrameToken => CurrentFrame.Token;

    public ushort Catch() => CurrentFrame.Token;

    public ushort GetLocal(int localNumber)
    {
        ValidateLocalNumber(localNumber);
        return CurrentFrame.Locals[localNumber - 1];
    }

    public void SetLocal(int localNumber, ushort value)
    {
        ValidateLocalNumber(localNumber);
        CurrentFrame.Locals[localNumber - 1] = value;
    }

    public bool HasArgument(int argumentNumber)
    {
        if (_header.Version < 5)
        {
            throw new ZMachineRuntimeException("check_arg_count is only available in versions 5 and later.");
        }

        return argumentNumber > 0 && argumentNumber <= CurrentFrame.ArgumentCount;
    }

    public void SetProgramCounter(int address)
    {
        ValidateProgramCounter(address);
        ProgramCounter = address;
    }

    public void AdvanceProgramCounter(int byteCount)
    {
        var target = (long)ProgramCounter + byteCount;
        if (target < 0 || target >= _memory.StoryLength)
        {
            throw new ZMachineRuntimeException($"Program counter advance to 0x{target:x} is outside the {_memory.StoryLength}-byte story.");
        }

        ProgramCounter = (int)target;
    }

    public RoutineCallResult CallRoutine(
        ushort packedAddress,
        IReadOnlyList<ushort> arguments,
        int returnAddress,
        byte? storeVariable = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ValidateProgramCounter(returnAddress);

        var maximumArguments = _header.Version <= 3 ? 3 : 7;
        if (arguments.Count > maximumArguments)
        {
            throw new ZMachineRuntimeException($"Version {_header.Version} routine calls accept at most {maximumArguments} arguments.");
        }

        if (packedAddress == 0)
        {
            ProgramCounter = returnAddress;
            return new RoutineCallResult(false, 0, storeVariable);
        }

        var routineAddress = ZMachineAddressing.DecodePackedAddress(packedAddress, _header, PackedAddressKind.Routine);
        var routine = ReadRoutine(routineAddress, returnAddress, storeVariable, arguments.Count, isRoot: false);
        EnsureCapacity(routine.Frame.StackUsage);

        for (var index = 0; index < Math.Min(arguments.Count, routine.Frame.Locals.Length); index++)
        {
            routine.Frame.Locals[index] = arguments[index];
        }

        routine.Frame.Token = AllocateFrameToken();
        _frames.Add(routine.Frame);
        _routineUsage += routine.Frame.StackUsage;
        ProgramCounter = routine.FirstInstructionAddress;
        return new RoutineCallResult(true, null, storeVariable);
    }

    public void Push(ushort value)
    {
        EnsureCapacity(1);
        _evaluationStack.Add(value);
    }

    public ushort Pop()
    {
        var frame = CurrentFrame;
        if (_evaluationStack.Count <= frame.EvaluationStackBase)
        {
            throw new ZMachineRuntimeException("The evaluation stack is empty for the current routine.");
        }

        var index = _evaluationStack.Count - 1;
        var value = _evaluationStack[index];
        _evaluationStack.RemoveAt(index);
        return value;
    }

    public ushort Peek()
    {
        var frame = CurrentFrame;
        if (_evaluationStack.Count <= frame.EvaluationStackBase)
        {
            throw new ZMachineRuntimeException("The evaluation stack is empty for the current routine.");
        }

        return _evaluationStack[^1];
    }

    public void ReplaceTop(ushort value)
    {
        var frame = CurrentFrame;
        if (_evaluationStack.Count <= frame.EvaluationStackBase)
        {
            throw new ZMachineRuntimeException("The evaluation stack is empty for the current routine.");
        }

        _evaluationStack[^1] = value;
    }

    public RoutineReturnResult ReturnFromRoutine(ushort value)
    {
        var frameIndex = _frames.Count - 1;
        if (frameIndex <= 0 || CurrentFrame.IsRoot)
        {
            throw new ZMachineRuntimeException("The initial execution environment cannot return.");
        }

        return CompleteFrame(frameIndex, value);
    }

    public RoutineReturnResult ReturnPopped()
    {
        if (CurrentFrame.IsRoot)
        {
            throw new ZMachineRuntimeException("The initial execution environment cannot return.");
        }

        var value = Pop();
        return ReturnFromRoutine(value);
    }

    public RoutineReturnResult Throw(ushort value, ushort stackFrame)
    {
        var frameIndex = _frames.FindIndex(frame => frame.Token == stackFrame);
        if (frameIndex <= 0)
        {
            throw new ZMachineRuntimeException($"Stack frame {stackFrame} is not an active routine that can return.");
        }

        return CompleteFrame(frameIndex, value);
    }

    public bool PushUserStack(ushort value, ushort stackAddress)
    {
        EnsureVersion6UserStack();
        _memory.ValidateDynamicRange(stackAddress, sizeof(ushort));
        var spareSlots = _memory.ReadWord(stackAddress);
        if (spareSlots == 0)
        {
            return false;
        }

        var valueAddress = (long)stackAddress + ((long)spareSlots * sizeof(ushort));
        if (valueAddress > ushort.MaxValue)
        {
            throw new ZMachineRuntimeException($"User stack at 0x{stackAddress:x4} exceeds addressable dynamic memory.");
        }

        _memory.WriteWord((ushort)valueAddress, value);
        _memory.WriteWord(stackAddress, (ushort)(spareSlots - 1));
        return true;
    }

    public ushort PullUserStack(ushort stackAddress)
    {
        EnsureVersion6UserStack();
        _memory.ValidateDynamicRange(stackAddress, sizeof(ushort));
        var spareSlots = _memory.ReadWord(stackAddress);
        var valueAddress = (long)stackAddress + ((long)(spareSlots + 1) * sizeof(ushort));
        if (valueAddress > ushort.MaxValue || spareSlots == ushort.MaxValue)
        {
            throw new ZMachineRuntimeException($"User stack at 0x{stackAddress:x4} has an invalid spare-slot count {spareSlots}.");
        }

        var value = _memory.ReadWord((ushort)valueAddress);
        _memory.WriteWord(stackAddress, (ushort)(spareSlots + 1));
        return value;
    }

    public void PopStack(ushort itemCount, ushort? userStackAddress = null)
    {
        if (userStackAddress.HasValue)
        {
            for (var index = 0; index < itemCount; index++)
            {
                _ = PullUserStack(userStackAddress.Value);
            }

            return;
        }

        if (itemCount > _evaluationStack.Count - CurrentFrame.EvaluationStackBase)
        {
            throw new ZMachineRuntimeException("Cannot discard more evaluation-stack values than the current routine has pushed.");
        }

        _evaluationStack.RemoveRange(_evaluationStack.Count - itemCount, itemCount);
    }

    private RoutineReturnResult CompleteFrame(int frameIndex, ushort value)
    {
        var returningFrame = _frames[frameIndex];
        if (returningFrame.IsRoot)
        {
            throw new ZMachineRuntimeException("The initial execution environment cannot return.");
        }

        _evaluationStack.RemoveRange(returningFrame.EvaluationStackBase, _evaluationStack.Count - returningFrame.EvaluationStackBase);
        for (var index = _frames.Count - 1; index >= frameIndex; index--)
        {
            _routineUsage -= _frames[index].StackUsage;
            _frames.RemoveAt(index);
        }

        ProgramCounter = returningFrame.ReturnAddress;
        return new RoutineReturnResult(value, returningFrame.ReturnAddress, returningFrame.StoreVariable);
    }

    private (RoutineFrame Frame, int FirstInstructionAddress) ReadRoutine(
        int routineAddress,
        int returnAddress,
        byte? storeVariable,
        int argumentCount,
        bool isRoot)
    {
        var localCount = _memory.ReadInstructionByte(routineAddress);
        if (localCount > 15)
        {
            throw new ZMachineRuntimeException($"Routine at 0x{routineAddress:x} declares {localCount} locals; the maximum is 15.");
        }

        var locals = new ushort[localCount];
        var firstInstructionAddress = routineAddress + 1;
        if (_header.Version <= 4)
        {
            var end = (long)routineAddress + 1 + (localCount * sizeof(ushort));
            if (end > _memory.StoryLength)
            {
                throw new ZMachineRuntimeException($"Routine local defaults at 0x{routineAddress:x} extend beyond the story file.");
            }

            for (var index = 0; index < localCount; index++)
            {
                var highByte = _memory.ReadInstructionByte(firstInstructionAddress + (index * sizeof(ushort)));
                var lowByte = _memory.ReadInstructionByte(firstInstructionAddress + (index * sizeof(ushort)) + 1);
                locals[index] = (ushort)((highByte << 8) | lowByte);
            }

            firstInstructionAddress = (int)end;
        }

        ValidateProgramCounter(firstInstructionAddress);
        var frame = new RoutineFrame(
            routineAddress,
            returnAddress,
            storeVariable,
            argumentCount,
            locals,
            _evaluationStack.Count,
            isRoot,
            0,
            4 + localCount);
        return (frame, firstInstructionAddress);
    }

    private void ValidateLocalNumber(int localNumber)
    {
        if (localNumber < 1 || localNumber > CurrentFrame.Locals.Length)
        {
            throw new ZMachineRuntimeException($"Local variable {localNumber} does not exist in the current routine.");
        }
    }

    private void ValidateProgramCounter(int address)
    {
        if ((uint)address >= (uint)_memory.StoryLength)
        {
            throw new ZMachineRuntimeException($"Program counter 0x{address:x} is outside the {_memory.StoryLength}-byte story.");
        }
    }

    private void EnsureCapacity(int additionalWords)
    {
        if (_routineUsage + _evaluationStack.Count + additionalWords > MaximumStackWords)
        {
            throw new ZMachineRuntimeException($"The Z-machine stack exceeds its {MaximumStackWords}-word capacity.");
        }
    }

    private ushort AllocateFrameToken()
    {
        for (var attempt = 0; attempt <= ushort.MaxValue; attempt++)
        {
            var candidate = _nextFrameToken++;
            if (_frames.All(frame => frame.Token != candidate))
            {
                return candidate;
            }
        }

        throw new ZMachineRuntimeException("No unique stack-frame token is available.");
    }

    private void EnsureVersion6UserStack()
    {
        if (_header.Version != 6)
        {
            throw new ZMachineRuntimeException("User stacks are only available in version 6.");
        }
    }

    private RoutineFrame CurrentFrame => _frames[^1];

    private sealed class RoutineFrame(
        int routineAddress,
        int returnAddress,
        byte? storeVariable,
        int argumentCount,
        ushort[] locals,
        int evaluationStackBase,
        bool isRoot,
        ushort token,
        int stackUsage)
    {
        public int RoutineAddress { get; } = routineAddress;

        public int ReturnAddress { get; } = returnAddress;

        public byte? StoreVariable { get; } = storeVariable;

        public int ArgumentCount { get; } = argumentCount;

        public ushort[] Locals { get; } = locals;

        public int EvaluationStackBase { get; } = evaluationStackBase;

        public bool IsRoot { get; } = isRoot;

        public ushort Token { get; set; } = token;

        public int StackUsage { get; } = stackUsage;

        public static RoutineFrame CreateEnvironment() => new(-1, 0, null, 0, [], 0, true, 0, 0);
    }
}
