namespace Leafpile.Core.Machine;

public sealed record ZMachineExecutionResult(
    bool Halted = false,
    bool Restarted = false,
    bool Restored = false,
    string? Output = null);
