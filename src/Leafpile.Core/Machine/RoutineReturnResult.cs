namespace Leafpile.Core.Machine;

public sealed record RoutineReturnResult(ushort Value, int ResumeAddress, byte? StoreVariable);
