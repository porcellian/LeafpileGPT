using System.Buffers.Binary;
using LeafpileGPT.Core;

namespace LeafpileGPT.Tests;

internal static class StoryFixtureBuilder
{
    public const int DefaultLength = 1_024;
    public const ushort DefaultStaticBase = 700;

    public static byte[] Create(
        byte version,
        int length = DefaultLength,
        ushort staticBase = DefaultStaticBase,
        bool includeLengthAndChecksum = true,
        ushort highMemoryBase = 800,
        ushort dictionaryAddress = 200,
        ushort objectTableAddress = 300,
        ushort globalsTableAddress = 64)
    {
        var story = new byte[length];
        story[0] = version;
        WriteWord(story, 0x02, 42);
        WriteWord(story, 0x04, highMemoryBase);
        WriteWord(story, 0x06, version == 6 ? (ushort)200 : (ushort)850);
        WriteWord(story, 0x08, dictionaryAddress);
        WriteWord(story, 0x0A, objectTableAddress);
        WriteWord(story, 0x0C, globalsTableAddress);
        WriteWord(story, 0x0E, staticBase);
        WriteWord(story, 0x12, 0x3834);
        WriteWord(story, 0x14, 0x3031);
        WriteWord(story, 0x16, 0x3032);

        if (version >= 3 && includeLengthAndChecksum)
        {
            var multiplier = version <= 3 ? 2 : version <= 5 ? 4 : 8;
            if (length % multiplier != 0)
            {
                throw new ArgumentException("Fixture length must be representable in the story header.", nameof(length));
            }

            WriteWord(story, 0x1A, checked((ushort)(length / multiplier)));
        }

        for (var i = StoryFile.HeaderLength; i < story.Length; i++)
        {
            story[i] = (byte)(i % 251);
        }

        if (version >= 3 && includeLengthAndChecksum)
        {
            WriteWord(story, 0x1C, CalculateChecksum(story));
        }

        return story;
    }

    public static ushort CalculateChecksum(ReadOnlySpan<byte> story)
    {
        uint sum = 0;
        foreach (var value in story[StoryFile.HeaderLength..])
        {
            sum = (sum + value) & ushort.MaxValue;
        }

        return (ushort)sum;
    }

    public static void WriteWord(Span<byte> story, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(story.Slice(offset, sizeof(ushort)), value);
}
