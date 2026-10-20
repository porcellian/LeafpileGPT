using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineArithmeticTests
{
    [Theory]
    [InlineData(0xffff, 1, 0)]
    [InlineData(0x7fff, 1, 0x8000)]
    public void AdditionWrapsToSixteenBits(ushort left, ushort right, ushort expected) =>
        Assert.Equal(expected, ZMachineArithmetic.Add(left, right));

    [Fact]
    public void SubtractionWrapsToSixteenBits() =>
        Assert.Equal(0xffff, ZMachineArithmetic.Subtract(0, 1));

    [Theory]
    [InlineData(0xfff5, 2, 0xffea)]
    [InlineData(0x8000, 2, 0)]
    public void MultipliesSignedValuesAndKeepsTheLowSixteenBits(ushort left, ushort right, ushort expected) =>
        Assert.Equal(expected, ZMachineArithmetic.Multiply(left, right));

    [Theory]
    [InlineData(0xfff5, 2, 0xfffb)]
    [InlineData(0xfff5, 0xfffe, 5)]
    [InlineData(11, 0xfffe, 0xfffb)]
    [InlineData(0x8000, 0xffff, 0x8000)]
    public void DividesAsSignedSixteenBitValues(ushort dividend, ushort divisor, ushort expected) =>
        Assert.Equal(expected, ZMachineArithmetic.Divide(dividend, divisor));

    [Theory]
    [InlineData(0xfff5, 5, 0xffff)]
    [InlineData(13, 0xfffb, 3)]
    [InlineData(0xfff3, 0xfffb, 0xfffd)]
    public void CalculatesSignedRemainders(ushort dividend, ushort divisor, ushort expected) =>
        Assert.Equal(expected, ZMachineArithmetic.Remainder(dividend, divisor));

    [Fact]
    public void RejectsDivisionAndRemainderByZero()
    {
        var divideError = Assert.Throws<ZMachineRuntimeException>(() => ZMachineArithmetic.Divide(3, 0));
        var remainderError = Assert.Throws<ZMachineRuntimeException>(() => ZMachineArithmetic.Remainder(3, 0));

        Assert.Contains("divide by zero", divideError.Message);
        Assert.Contains("division by zero", remainderError.Message);
    }

    [Fact]
    public void ComparesSignedValuesAndKeepsBitwiseOperationsUnsigned()
    {
        Assert.True(ZMachineArithmetic.CompareSigned(0xffff, 0) < 0);
        Assert.Equal(0x8000, ZMachineArithmetic.And(0xf0f0, 0x8f0f));
        Assert.Equal(0x8fff, ZMachineArithmetic.Or(0x800f, 0x0ff0));
        Assert.Equal(0x0f0f, ZMachineArithmetic.Not(0xf0f0));
    }
}
