namespace Leafpile.Core.Machine;

public sealed record RoutineCallResult(bool RoutineStarted, ushort? ImmediateReturnValue, byte? StoreVariable);
