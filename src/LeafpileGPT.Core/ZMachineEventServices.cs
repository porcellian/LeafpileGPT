namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>Snapshot of the current Z-machine screen-relative mouse and menu state.</summary>
public readonly record struct ZMachineMouseState(short Y, short X, ushort ButtonBits, ushort MenuSelection = 0)
{
    public static ZMachineMouseState Offscreen { get; } = new(-1, -1, 0);
}

public enum ZMachineSoundAction : ushort
{
    Prepare = 1,
    Start = 2,
    Stop = 3,
    FinishWith = 4
}

/// <summary>A versioned sound request, including the standard volume and repeat encoding.</summary>
public sealed record ZMachineSoundEffectRequest(
    ushort Number,
    ZMachineSoundAction Action,
    byte Volume,
    byte Repeats,
    ushort CompletionRoutine);

/// <summary>Supplies optional platform mouse, menu, and sound operations to the VM.</summary>
public interface IZMachineEventServices
{
    bool SupportsMouse { get; }

    bool SupportsMenus { get; }

    bool SupportsSoundEffects { get; }

    ZMachineMouseState ReadMouse();

    void SetMouseWindow(short window);

    bool MakeMenu(ushort number, ImmutableArray<string> entries);

    void PerformSoundEffect(ZMachineSoundEffectRequest request, Action? completed);
}

/// <summary>No-op optional event services for hosts that have not supplied platform features.</summary>
public sealed class NullZMachineEventServices : IZMachineEventServices
{
    public static NullZMachineEventServices Instance { get; } = new();

    private NullZMachineEventServices()
    {
    }

    public bool SupportsMouse => false;

    public bool SupportsMenus => false;

    public bool SupportsSoundEffects => false;

    public ZMachineMouseState ReadMouse() => ZMachineMouseState.Offscreen;

    public void SetMouseWindow(short window)
    {
    }

    public bool MakeMenu(ushort number, ImmutableArray<string> entries) => false;

    public void PerformSoundEffect(ZMachineSoundEffectRequest request, Action? completed)
    {
    }
}
