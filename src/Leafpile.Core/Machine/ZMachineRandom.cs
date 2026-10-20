using System.Security.Cryptography;

namespace Leafpile.Core.Machine;

public sealed class ZMachineRandom
{
    private Random? _seededRandom;
    private int _smallSeed;
    private int _smallSequenceIndex;
    private bool _predictable;

    public bool IsPredictable => _predictable;

    public void ResetToRandom()
    {
        _predictable = false;
        _seededRandom = null;
        _smallSeed = 0;
        _smallSequenceIndex = 0;
    }

    public ushort Next(ushort rawRange)
    {
        var range = ZMachineArithmetic.ToSigned(rawRange);
        if (range == 0)
        {
            ResetToRandom();
            return 0;
        }

        if (range < 0)
        {
            SeedPredictably(-(int)range);
            return 0;
        }

        if (!_predictable)
        {
            return (ushort)RandomNumberGenerator.GetInt32(1, range + 1);
        }

        if (_smallSeed > 0)
        {
            var sequenceValue = _smallSequenceIndex + 1;
            _smallSequenceIndex = (_smallSequenceIndex + 1) % _smallSeed;
            return (ushort)(((sequenceValue - 1) % range) + 1);
        }

        return (ushort)(_seededRandom!.Next(range) + 1);
    }

    private void SeedPredictably(int seed)
    {
        _predictable = true;
        _seededRandom = null;
        _smallSeed = seed < 1000 ? seed : 0;
        _smallSequenceIndex = 0;
        if (_smallSeed == 0)
        {
            _seededRandom = new Random(seed);
        }
    }
}
