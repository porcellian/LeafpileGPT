namespace Leafpile.Core.Machine;

public sealed class ZMachineRuntimeException : Exception
{
    public ZMachineRuntimeException(string message)
        : base(message)
    {
    }
}
