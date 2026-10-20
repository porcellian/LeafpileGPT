namespace Leafpile.Core.Machine;

public sealed record ZMachineSaveRestoreOutcome(bool Succeeded, ushort ReturnValue, bool ResumedFromSavedState = false);

public interface IZMachineSaveRestoreService
{
    ZMachineSaveRestoreOutcome Save(ZMachineState state, ZMachineInstruction instruction, bool undo);

    ZMachineSaveRestoreOutcome Restore(ZMachineState state, ZMachineInstruction instruction, bool undo);
}

/// <summary>Provides save-file selection and byte transfer without tying the core to a desktop UI.</summary>
public interface IZMachineSaveFileAccess
{
    bool WriteSaveFile(ReadOnlyMemory<byte> saveFile);

    ReadOnlyMemory<byte>? ReadSaveFile();
}
