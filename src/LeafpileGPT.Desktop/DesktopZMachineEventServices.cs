using System.Collections.Immutable;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Desktop event bridge for asynchronous audio requests and unsupported optional events.</summary>
public sealed class DesktopZMachineEventServices : IZMachineEventServices
{
    public event Action<ZMachineSoundEffectRequest>? SoundEffectRequested;

    public bool SupportsMouse => false;

    public bool SupportsMenus => false;

    // No cross-platform decoder/output backend is included in the desktop shell yet.
    public bool SupportsSoundEffects => false;

    public ZMachineMouseState ReadMouse() => ZMachineMouseState.Offscreen;

    public void SetMouseWindow(short window)
    {
    }

    public bool MakeMenu(ushort number, ImmutableArray<string> entries) => false;

    public void PerformSoundEffect(ZMachineSoundEffectRequest request, Action? completed) =>
        SoundEffectRequested?.Invoke(request);
}
