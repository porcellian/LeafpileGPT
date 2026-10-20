using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineRandomTests
{
    [Fact]
    public void RandomModeReturnsValuesWithinTheRequestedRange()
    {
        var random = new ZMachineRandom();

        for (var index = 0; index < 500; index++)
        {
            var value = random.Next(17);
            Assert.InRange(value, (ushort)1, (ushort)17);
        }
    }

    [Fact]
    public void SmallPredictableSeedProducesRepeatingRisingSequenceModuloRange()
    {
        var random = new ZMachineRandom();

        Assert.Equal(0, random.Next(unchecked((ushort)-10)));
        Assert.True(random.IsPredictable);
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 1, 2, 3, 4 }, Enumerable.Range(0, 8).Select(_ => random.Next(4)).ToArray());
    }

    [Fact]
    public void LargePredictableSeedsProduceRepeatableSequences()
    {
        var first = new ZMachineRandom();
        var second = new ZMachineRandom();
        first.Next(unchecked((ushort)-1234));
        second.Next(unchecked((ushort)-1234));

        var firstSequence = Enumerable.Range(0, 20).Select(_ => first.Next(1000)).ToArray();
        var secondSequence = Enumerable.Range(0, 20).Select(_ => second.Next(1000)).ToArray();

        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public void ZeroResetsTheGeneratorToRandomMode()
    {
        var random = new ZMachineRandom();
        random.Next(unchecked((ushort)-20));

        Assert.Equal(0, random.Next(0));

        Assert.False(random.IsPredictable);
        Assert.InRange(random.Next(1), (ushort)1, (ushort)1);
    }

    [Fact]
    public void MinimumSignedOperandCanBeUsedAsAPredictableSeed()
    {
        var random = new ZMachineRandom();

        Assert.Equal(0, random.Next(0x8000));

        Assert.True(random.IsPredictable);
        Assert.InRange(random.Next(100), (ushort)1, (ushort)100);
    }
}
