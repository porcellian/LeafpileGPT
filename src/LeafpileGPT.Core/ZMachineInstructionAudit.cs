using System.Collections.Immutable;

namespace LeafpileGPT.Core;

/// <summary>A command and the instructions executed while handling it.</summary>
public sealed record ZMachineInstructionAuditGroup(
    long Number,
    string Command,
    DateTimeOffset StartedAt,
    ImmutableArray<ZMachineInstructionAuditEntry> Instructions);

public sealed record ZMachineInstructionAuditEntry(
    long Sequence,
    uint ProgramCounter,
    InstructionForm Form,
    OpcodeClass OpcodeClass,
    byte Opcode,
    string Mnemonic,
    ImmutableArray<DecodedOperand> Operands,
    byte? StoreVariable,
    DecodedBranch? Branch,
    uint NextProgramCounter,
    uint ProgramCounterAfter,
    bool IsHalted);

public sealed record ZMachineInstructionAuditGroupInfo(
    long Number,
    string Command,
    DateTimeOffset StartedAt,
    int InstructionCount);

public enum ZMachineInstructionAuditEventKind
{
    CommandStarted,
    InstructionRecorded,
    HistoryCleared
}

public sealed record ZMachineInstructionAuditEvent(
    ZMachineInstructionAuditEventKind Kind,
    long GroupNumber,
    string? Command,
    DateTimeOffset? StartedAt,
    ZMachineInstructionAuditEntry? Instruction,
    long? RelatedGroupNumber = null);

/// <summary>Compact, thread-safe instruction history for one live game session.</summary>
public sealed class ZMachineInstructionAudit
{
    private readonly object _gate = new();
    private readonly List<MutableGroup> _groups = [];
    private readonly Queue<ZMachineInstructionAuditEvent> _pendingEvents = new();
    private long _nextGroupNumber;
    private long _nextInstructionSequence;
    private bool _publishingEvents;

    public ZMachineInstructionAudit() => BeginCommand("Game started");

    public event Action? Changed;

    /// <summary>Ordered command and instruction notifications for external audit sinks.</summary>
    public event Action<ZMachineInstructionAuditEvent>? EventRecorded;

    public long InstructionsExecuted
    {
        get
        {
            lock (_gate)
            {
                return _nextInstructionSequence;
            }
        }
    }

    public int GroupCount
    {
        get
        {
            lock (_gate)
            {
                return _groups.Count;
            }
        }
    }

    public void BeginCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            var group = new MutableGroup(++_nextGroupNumber, command, DateTimeOffset.UtcNow);
            _groups.Add(group);
            _pendingEvents.Enqueue(new ZMachineInstructionAuditEvent(
                ZMachineInstructionAuditEventKind.CommandStarted,
                group.Number,
                group.Command,
                group.StartedAt,
                null));
        }

        PublishQueuedAuditEvents();
        NotifyChanged();
    }

    public ZMachineInstructionAuditEntry Record(DecodedInstruction instruction, ZMachineExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(state);
        ZMachineInstructionAuditEntry entry;
        long groupNumber;
        lock (_gate)
        {
            if (_groups.Count == 0)
            {
                _groups.Add(new MutableGroup(++_nextGroupNumber, "Recording continued", DateTimeOffset.UtcNow));
            }

            var sequence = ++_nextInstructionSequence;
            entry = new ZMachineInstructionAuditEntry(
                sequence,
                instruction.ProgramCounter,
                instruction.Form,
                instruction.Class,
                instruction.Opcode,
                instruction.Definition.Mnemonic,
                instruction.Operands,
                instruction.StoreVariable,
                instruction.Branch,
                instruction.NextProgramCounter,
                state.ProgramCounter,
                state.IsHalted);
            _groups[^1].Instructions.Add(entry);
            groupNumber = _groups[^1].Number;
            _pendingEvents.Enqueue(new ZMachineInstructionAuditEvent(
                ZMachineInstructionAuditEventKind.InstructionRecorded,
                groupNumber,
                null,
                null,
                entry));
        }

        PublishQueuedAuditEvents();
        NotifyChanged();
        return entry;
    }

    public ImmutableArray<ZMachineInstructionAuditGroup> GetSnapshot()
    {
        lock (_gate)
        {
            return _groups.Select(group => new ZMachineInstructionAuditGroup(
                group.Number,
                group.Command,
                group.StartedAt,
                group.Instructions.ToImmutableArray())).ToImmutableArray();
        }
    }

    public ZMachineInstructionAuditGroup? GetGroupSnapshot(int index)
        => GetGroupSnapshot(index, 0);

    public ZMachineInstructionAuditGroup? GetGroupSnapshot(int index, int firstInstructionIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstInstructionIndex);
        lock (_gate)
        {
            if ((uint)index >= (uint)_groups.Count)
            {
                return null;
            }

            var group = _groups[index];
            return new ZMachineInstructionAuditGroup(
                group.Number,
                group.Command,
                group.StartedAt,
                group.Instructions.Skip(firstInstructionIndex).ToImmutableArray());
        }
    }

    public int GetInstructionCount(int groupIndex)
    {
        lock (_gate)
        {
            return (uint)groupIndex < (uint)_groups.Count ? _groups[groupIndex].Instructions.Count : 0;
        }
    }

    public ZMachineInstructionAuditGroupInfo? GetGroupInfo(int groupIndex)
    {
        lock (_gate)
        {
            if ((uint)groupIndex >= (uint)_groups.Count)
            {
                return null;
            }

            var group = _groups[groupIndex];
            return new ZMachineInstructionAuditGroupInfo(
                group.Number,
                group.Command,
                group.StartedAt,
                group.Instructions.Count);
        }
    }

    /// <summary>Clears visible history while preserving global sequence numbers and live recording.</summary>
    public void ClearHistory()
    {
        lock (_gate)
        {
            var current = _groups.Count == 0 ? null : _groups[^1];
            _groups.Clear();
            var continuation = new MutableGroup(
                ++_nextGroupNumber,
                current?.Command ?? "Recording continued",
                DateTimeOffset.UtcNow);
            _groups.Add(continuation);
            _pendingEvents.Enqueue(new ZMachineInstructionAuditEvent(
                ZMachineInstructionAuditEventKind.HistoryCleared,
                continuation.Number,
                null,
                null,
                null,
                current?.Number));
        }

        PublishQueuedAuditEvents();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        var observers = Changed?.GetInvocationList();
        if (observers is null)
        {
            return;
        }

        foreach (var observer in observers)
        {
            try
            {
                ((Action)observer)();
            }
            catch (Exception)
            {
                // Diagnostic viewers must never change game execution behavior.
            }
        }
    }

    private void PublishQueuedAuditEvents()
    {
        lock (_gate)
        {
            if (_publishingEvents || _pendingEvents.Count == 0)
            {
                return;
            }

            _publishingEvents = true;
        }

        while (true)
        {
            ZMachineInstructionAuditEvent auditEvent;
            lock (_gate)
            {
                if (_pendingEvents.Count == 0)
                {
                    _publishingEvents = false;
                    return;
                }

                auditEvent = _pendingEvents.Dequeue();
            }

            var observers = EventRecorded?.GetInvocationList();
            if (observers is null)
            {
                continue;
            }

            foreach (var observer in observers)
            {
                try
                {
                    ((Action<ZMachineInstructionAuditEvent>)observer)(auditEvent);
                }
                catch (Exception)
                {
                    // Audit sinks must never change game execution behavior.
                }
            }
        }
    }

    private sealed class MutableGroup(long number, string command, DateTimeOffset startedAt)
    {
        public long Number { get; } = number;

        public string Command { get; } = command;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public List<ZMachineInstructionAuditEntry> Instructions { get; } = [];
    }
}
