namespace LeafpileGPT.Core;

/// <summary>Tracks command-group selection and live-follow behavior in the instruction viewer.</summary>
public sealed class ZMachineInstructionAuditNavigation
{
    public int SelectedIndex { get; private set; } = -1;

    public bool IsFollowing { get; private set; } = true;

    public void GroupsChanged(int groupCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCount);
        if (groupCount == 0)
        {
            SelectedIndex = -1;
        }
        else if (IsFollowing || SelectedIndex < 0)
        {
            SelectedIndex = groupCount - 1;
        }
        else
        {
            SelectedIndex = Math.Min(SelectedIndex, groupCount - 1);
        }
    }

    public void Previous(int groupCount)
    {
        GroupsChanged(groupCount);
        if (SelectedIndex > 0)
        {
            SelectedIndex--;
            IsFollowing = false;
        }
    }

    public void Current(int groupCount)
    {
        GroupsChanged(groupCount);
        IsFollowing = true;
        if (groupCount > 0)
        {
            SelectedIndex = groupCount - 1;
        }
    }

    public void Next(int groupCount)
    {
        GroupsChanged(groupCount);
        if (SelectedIndex >= 0 && SelectedIndex < groupCount - 1)
        {
            SelectedIndex++;
            IsFollowing = false;
        }
    }
}
