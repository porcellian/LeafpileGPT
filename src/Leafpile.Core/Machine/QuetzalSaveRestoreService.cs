namespace Leafpile.Core.Machine;

/// <summary>Connects the IFZS codec to a host's save-file picker and implements in-memory undo.</summary>
public sealed class QuetzalSaveRestoreService(IZMachineSaveFileAccess files) : IZMachineSaveRestoreService
{
    private byte[]? _undoSave;

    public ZMachineSaveRestoreOutcome Save(ZMachineState state, ZMachineInstruction instruction, bool undo)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instruction);
        var resumeAddress = GetResumeAddress(state, instruction);
        var saveFile = QuetzalSaveFile.Write(state, resumeAddress);
        if (undo)
        {
            _undoSave = saveFile;
            return new ZMachineSaveRestoreOutcome(true, 1);
        }

        var succeeded = files.WriteSaveFile(saveFile);
        return new ZMachineSaveRestoreOutcome(succeeded, succeeded ? (ushort)1 : (ushort)0);
    }

    public ZMachineSaveRestoreOutcome Restore(ZMachineState state, ZMachineInstruction instruction, bool undo)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(instruction);
        var saveFile = undo ? _undoSave : files.ReadSaveFile()?.ToArray();
        if (saveFile is null)
        {
            return new ZMachineSaveRestoreOutcome(false, 0);
        }

        QuetzalSaveDocument document;
        try
        {
            document = QuetzalSaveFile.Read(saveFile, state.LoadedStory);
            QuetzalSaveFile.Restore(state, document);
        }
        catch (QuetzalSaveFileException)
        {
            return new ZMachineSaveRestoreOutcome(false, 0);
        }
        catch (ZMachineRuntimeException)
        {
            return new ZMachineSaveRestoreOutcome(false, 0);
        }

        if (undo) _undoSave = null;
        return new ZMachineSaveRestoreOutcome(true, 2, ResumedFromSavedState: true);
    }

    private static int GetResumeAddress(ZMachineState state, ZMachineInstruction instruction)
    {
        var address = instruction.NextAddress - 1;
        if (state.Header.Version <= 3)
        {
            if (instruction.Branch is null)
            {
                throw new ZMachineRuntimeException("The save instruction does not include its branch continuation.");
            }

            address = instruction.BranchAddress
                ?? throw new ZMachineRuntimeException("The save instruction does not include its branch descriptor address.");
        }
        else if (instruction.StoreVariable is null)
        {
            throw new ZMachineRuntimeException("The save instruction does not include its result-variable descriptor.");
        }

        if (address < 0)
        {
            throw new ZMachineRuntimeException("The save instruction has no valid continuation descriptor.");
        }

        return address;
    }
}
