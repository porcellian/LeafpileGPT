namespace Leafpile.Core.Machine;

/// <summary>Receives renderer-neutral screen and runtime events from a desktop host.</summary>
public interface IZMachineDisplay
{
    void Present(ZMachineScreen screen);

    void ReportRuntimeError(string message);
}
