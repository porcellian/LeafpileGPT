namespace Leafpile.Core.Machine;

public sealed record ZMachineSaveRestoreOutcome(bool Succeeded, ushort ReturnValue, bool ResumedFromSavedState = false);

public interface IZMachineSaveRestoreService
{
    ZMachineSaveRestoreOutcome Save(ZMachineState state, ZMachineInstruction instruction, bool undo);

    ZMachineSaveRestoreOutcome Restore(ZMachineState state, ZMachineInstruction instruction, bool undo);
}
