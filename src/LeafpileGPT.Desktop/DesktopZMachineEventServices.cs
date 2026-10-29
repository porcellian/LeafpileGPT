using System.Collections.Immutable;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Desktop event bridge for asynchronous audio requests and unsupported optional events.</summary>
public sealed class DesktopZMachineEventServices : IZMachineEventServices, IDisposable
{
    private readonly IDesktopBleepOutput _bleepOutput;

    public DesktopZMachineEventServices(IDesktopBleepOutput? bleepOutput = null)
    {
        _bleepOutput = bleepOutput ?? new SdlDesktopBleepOutput();
    }

    public event Action<string>? SoundPlaybackUnavailable;

    public bool SupportsMouse => false;

    public bool SupportsMenus => false;

    public bool SupportsSoundEffects => _bleepOutput.IsAvailable;

    public ZMachineMouseState ReadMouse() => ZMachineMouseState.Offscreen;

    public void SetMouseWindow(short window)
    {
    }

    public bool MakeMenu(ushort number, ImmutableArray<string> entries) => false;

    public void PerformSoundEffect(ZMachineSoundEffectRequest request, Action? completed)
    {
        if (request.Number is 1 or 2 && request.Resource is null && request.Action == ZMachineSoundAction.Start)
        {
            if (!_bleepOutput.IsAvailable)
            {
                SoundPlaybackUnavailable?.Invoke(
                    $"Audio playback is unavailable: {_bleepOutput.UnavailableReason ?? "the default output device could not be opened"}");
                return;
            }

            try
            {
                _bleepOutput.Play(request.Number);
            }
            catch (Exception exception)
            {
                SoundPlaybackUnavailable?.Invoke($"Could not play sound effect {request.Number}: {exception.Message}");
            }

            return;
        }

        var description = request.Resource is null
            ? $"sound effect {request.Number}"
            : $"{request.Resource.Format} sound {request.Number}";
        var reason = request.Number is 1 or 2 && _bleepOutput.UnavailableReason is not null
            ? $"Audio playback is unavailable: {_bleepOutput.UnavailableReason}"
            : $"The story requested {description}; Blorb audio playback is not available yet.";
        SoundPlaybackUnavailable?.Invoke(reason);
    }

    public void Dispose() => _bleepOutput.Dispose();
}
