namespace Leafpile.Core.Machine;

public static class ZMachineArithmetic
{
    public static short ToSigned(ushort value) => unchecked((short)value);

    public static ushort FromSigned(int value) => unchecked((ushort)value);

    public static ushort Add(ushort left, ushort right) => unchecked((ushort)(left + right));

    public static ushort Subtract(ushort left, ushort right) => unchecked((ushort)(left - right));

    public static ushort Multiply(ushort left, ushort right) =>
        FromSigned(ToSigned(left) * ToSigned(right));

    public static ushort Divide(ushort dividend, ushort divisor)
    {
        var signedDivisor = ToSigned(divisor);
        if (signedDivisor == 0)
        {
            throw new ZMachineRuntimeException("The Z-machine cannot divide by zero.");
        }

        return FromSigned((int)ToSigned(dividend) / signedDivisor);
    }

    public static ushort Remainder(ushort dividend, ushort divisor)
    {
        var signedDivisor = ToSigned(divisor);
        if (signedDivisor == 0)
        {
            throw new ZMachineRuntimeException("The Z-machine cannot calculate a remainder after division by zero.");
        }

        return FromSigned((int)ToSigned(dividend) % signedDivisor);
    }

    public static int CompareSigned(ushort left, ushort right) =>
        ToSigned(left).CompareTo(ToSigned(right));

    public static ushort And(ushort left, ushort right) => (ushort)(left & right);

    public static ushort Or(ushort left, ushort right) => (ushort)(left | right);

    public static ushort Not(ushort value) => (ushort)~value;
}
