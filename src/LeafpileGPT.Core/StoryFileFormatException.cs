namespace LeafpileGPT.Core;

public class StoryFileFormatException : Exception
{
    public StoryFileFormatException(string message)
        : base(message)
    {
    }
}

public sealed class StoryFileChecksumException : StoryFileFormatException
{
    public StoryFileChecksumException(ushort expected, ushort actual)
        : base($"Story checksum mismatch: header contains 0x{expected:X4}, calculated 0x{actual:X4}.")
    {
        ExpectedChecksum = expected;
        ActualChecksum = actual;
    }

    public ushort ExpectedChecksum { get; }

    public ushort ActualChecksum { get; }
}
