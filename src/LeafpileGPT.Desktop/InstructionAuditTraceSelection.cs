using System.Collections.Immutable;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Tracks whether the viewer displays live instructions or a read-only imported trace.</summary>
public sealed class InstructionAuditTraceSelection
{
    private ZMachineInstructionAudit? _liveAudit;
    private ImmutableArray<ZMachineInstructionAuditGroup>? _archivedGroups;

    public bool IsShowingArchive => _archivedGroups is not null;

    public bool CanReturnToLive => _liveAudit is not null;

    public bool CanClear => _liveAudit is not null && !IsShowingArchive;

    public int GroupCount => _archivedGroups?.Length ?? _liveAudit?.GroupCount ?? 0;

    public void SetLiveAudit(ZMachineInstructionAudit? audit) => _liveAudit = audit;

    public void LoadArchive(ImmutableArray<ZMachineInstructionAuditGroup> groups)
    {
        if (groups.IsDefaultOrEmpty)
        {
            throw new ArgumentException("An imported audit must contain at least one command group.", nameof(groups));
        }

        _archivedGroups = groups;
    }

    public void ReturnToLive() => _archivedGroups = null;

    public ZMachineInstructionAuditGroupInfo? GetGroupInfo(int index)
    {
        if (_archivedGroups is null)
        {
            return _liveAudit?.GetGroupInfo(index);
        }

        if ((uint)index >= (uint)_archivedGroups.Value.Length)
        {
            return null;
        }

        var group = _archivedGroups.Value[index];
        return new ZMachineInstructionAuditGroupInfo(group.Number, group.Command, group.StartedAt, group.Instructions.Length);
    }

    public ZMachineInstructionAuditGroup? GetGroupSnapshot(int index, int firstInstructionIndex = 0)
    {
        if (_archivedGroups is null)
        {
            return _liveAudit?.GetGroupSnapshot(index, firstInstructionIndex);
        }

        if ((uint)index >= (uint)_archivedGroups.Value.Length)
        {
            return null;
        }

        var group = _archivedGroups.Value[index];
        return group with { Instructions = group.Instructions.Skip(firstInstructionIndex).ToImmutableArray() };
    }

    public IReadOnlyList<ZMachineInstructionAuditGroup> GetSnapshot() =>
        _archivedGroups is { } archived ? archived : _liveAudit?.GetSnapshot() ?? [];

    public void ClearLiveHistory()
    {
        if (!CanClear)
        {
            throw new InvalidOperationException("Only the live audit history can be cleared.");
        }

        _liveAudit!.ClearHistory();
    }
}
