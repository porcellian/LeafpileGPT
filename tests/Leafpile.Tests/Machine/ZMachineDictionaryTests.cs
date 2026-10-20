using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineDictionaryTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(8, true)]
    public void LooksUpSortedAndUnsortedVersionedDictionaryEntries(byte version, bool unsorted)
    {
        var words = new[] { "apple", "go", "zoo" };
        if (unsorted)
        {
            Array.Reverse(words);
        }

        var story = CreateDictionaryStory(version, words, unsorted);
        var loaded = StoryLoader.Load(story);
        var state = new ZMachineState(loaded);
        var dictionary = new ZMachineDictionary(state.Memory, state.Header, state.Characters);
        var encoded = state.Text.EncodeDictionaryText("GO"u8);

        Assert.Equal(3, dictionary.GetEntryCount(0x400));
        Assert.Equal(DictionaryEntryAddress(version, 1), dictionary.Lookup(0x400, encoded));
        Assert.Equal(0, dictionary.Lookup(0x400, state.Text.EncodeDictionaryText("missing"u8)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void TokenisesWordsAndSeparatorsWithVersionedTextBuffers(byte version)
    {
        var story = CreateDictionaryStory(version, ["go", ",", "."], unsorted: true, separators: [',', '.']);
        var state = new ZMachineState(StoryLoader.Load(story));
        var textBuffer = (ushort)0x100;
        var parseBuffer = (ushort)0x150;
        WriteTextBuffer(state.Memory, textBuffer, version, "Go, unknown.");
        state.Memory.WriteByte(parseBuffer, 8);
        var parser = CreateParser(state);

        parser.Tokenise(textBuffer, parseBuffer, 0x400, skipUnrecognized: false);

        Assert.Equal(4, state.Memory.ReadByte((ushort)(parseBuffer + 1)));
        var initialTextPosition = version <= 4 ? (byte)1 : (byte)2;
        AssertParseEntry(state.Memory, parseBuffer, 0, DictionaryEntryAddress(version, 0, 2), 2, initialTextPosition);
        AssertParseEntry(state.Memory, parseBuffer, 1, DictionaryEntryAddress(version, 1, 2), 1, (byte)(initialTextPosition + 2));
        AssertParseEntry(state.Memory, parseBuffer, 2, 0, 7, (byte)(initialTextPosition + 4));
        AssertParseEntry(state.Memory, parseBuffer, 3, DictionaryEntryAddress(version, 2, 2), 1, (byte)(initialTextPosition + 11));
    }

    [Fact]
    public void SkipUnrecognizedLeavesItsParseSlotUntouchedWithoutChangingTokenPositions()
    {
        var story = CreateDictionaryStory(5, ["go", ",", "."], unsorted: true, separators: [',', '.']);
        var state = new ZMachineState(StoryLoader.Load(story));
        const ushort parseBuffer = 0x150;
        WriteTextBuffer(state.Memory, 0x100, 5, "go unknown.");
        state.Memory.WriteByte(parseBuffer, 8);
        state.Memory.WriteWord((ushort)(parseBuffer + 2 + (1 * 4)), 0xbeef);
        state.Memory.WriteByte((ushort)(parseBuffer + 4 + (1 * 4)), 0xaa);
        state.Memory.WriteByte((ushort)(parseBuffer + 5 + (1 * 4)), 0xbb);

        CreateParser(state).Tokenise(0x100, parseBuffer, 0x400, skipUnrecognized: true);

        Assert.Equal(3, state.Memory.ReadByte((ushort)(parseBuffer + 1)));
        Assert.Equal(DictionaryEntryAddress(5, 0, 2), state.Memory.ReadWord((ushort)(parseBuffer + 2)));
        Assert.Equal(0xbeef, state.Memory.ReadWord((ushort)(parseBuffer + 6)));
        Assert.Equal(0xaa, state.Memory.ReadByte((ushort)(parseBuffer + 8)));
        Assert.Equal(0xbb, state.Memory.ReadByte((ushort)(parseBuffer + 9)));
        Assert.Equal(DictionaryEntryAddress(5, 2, 2), state.Memory.ReadWord((ushort)(parseBuffer + 10)));
        Assert.Equal(12, state.Memory.ReadByte((ushort)(parseBuffer + 13)));
    }

    [Fact]
    public void StopsAtParseBufferWordCapacity()
    {
        var story = CreateDictionaryStory(5, ["go"], unsorted: false);
        var state = new ZMachineState(StoryLoader.Load(story));
        WriteTextBuffer(state.Memory, 0x100, 5, "go unknown");
        state.Memory.WriteByte(0x150, 1);
        state.Memory.WriteWord(0x156, 0xbeef);

        CreateParser(state).Tokenise(0x100, 0x150, 0, skipUnrecognized: false);

        Assert.Equal(1, state.Memory.ReadByte(0x151));
        Assert.Equal(DictionaryEntryAddress(5, 0), state.Memory.ReadWord(0x152));
        Assert.Equal(0xbeef, state.Memory.ReadWord(0x156));
    }

    [Fact]
    public void EncodeTextUsesSourceOffsetAndWritesDictionaryFormBytes()
    {
        var code = new byte[] { 0xfc, 0x14, 0x01, 0x00, 2, 1, 0x01, 0x20 };
        var story = MachineStoryFactory.Create(5);
        story[0x100] = (byte)'x';
        story[0x101] = (byte)'G';
        story[0x102] = (byte)'O';
        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var expected = state.Text.EncodeDictionaryText("go"u8);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], state.Memory.ReadByte((ushort)(0x120 + index)));
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void ExecutesTokeniseOpcodeUsingDefaultDictionary(byte version)
    {
        var story = CreateDictionaryStory(version, ["go"], unsorted: false);
        var code = new byte[] { 0xfb, 0x05, 0x01, 0x00, 0x01, 0x50, 0, 0 };
        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        WriteTextBuffer(state.Memory, 0x100, version, "Go");
        state.Memory.WriteByte(0x150, 4);
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();

        Assert.Equal(1, state.Memory.ReadByte(0x151));
        Assert.Equal(DictionaryEntryAddress(version, 0), state.Memory.ReadWord(0x152));
        Assert.Equal(2, state.Memory.ReadByte(0x154));
        Assert.Equal(version <= 4 ? 1 : 2, state.Memory.ReadByte(0x155));
    }

    [Fact]
    public void ExecutesCheckUnicodeExtendedOpcode()
    {
        var story = MachineStoryFactory.Create(5);
        new byte[] { 0xbe, 12, 0x7f, (byte)'A', 0x10 }.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();

        Assert.Equal(3, state.ReadVariableValue(16));
    }

    [Theory]
    [InlineData(4, 0x82, 0x102)]
    [InlineData(5, 0x03, 0x103)]
    public void ScanTableSearchesVersionedWordAndByteFields(byte version, byte form, ushort expectedAddress)
    {
        var story = MachineStoryFactory.Create(version);
        WriteWord(story, 0x100, version == 4 ? (ushort)0xaaaa : (ushort)0x0010);
        if (version == 4)
        {
            WriteWord(story, 0x102, 0x1234);
        }
        else
        {
            story[0x101] = 0x11;
            story[0x103] = 0x42;
        }

        var code = version == 4
            ? new byte[] { 0xf7, 0x07, 0x12, 0x34, 0x01, 0x00, 2, 0x10, 0xc2 }
            : new byte[] { 0xf7, 0x45, 0x42, 0x01, 0x00, 2, form, 0x10, 0xc2 };

        code.CopyTo(story, 0x200);
        var state = new ZMachineState(StoryLoader.Load(story));
        var executor = new ZMachineOpcodeExecutor(state);

        executor.ExecuteNext();

        Assert.Equal(expectedAddress, state.ReadVariableValue(16));
    }

    [Theory]
    [InlineData((ushort)'A', 3)]
    [InlineData((ushort)'\n', 3)]
    [InlineData(8, 2)]
    [InlineData((ushort)'☃', 0)]
    public void CheckUnicodeReportsPrintAndKeyboardSupport(ushort unicodeCharacter, ushort expected)
    {
        var state = new ZMachineState(StoryLoader.Load(MachineStoryFactory.Create(5)));
        Assert.Equal(expected, state.Characters.CheckUnicode(unicodeCharacter));
    }

    [Fact]
    public void RejectsMalformedDictionaryLengthsAndTextBufferBounds()
    {
        var story = CreateDictionaryStory(5, ["go"], unsorted: false);
        story[0x401] = 5;
        var state = new ZMachineState(StoryLoader.Load(story));
        var dictionary = new ZMachineDictionary(state.Memory, state.Header, state.Characters);

        Assert.Throws<ZMachineRuntimeException>(() => dictionary.GetEntryCount(0x400));

        story = CreateDictionaryStory(5, ["go"], unsorted: false);
        state = new ZMachineState(StoryLoader.Load(story));
        state.Memory.WriteByte(0x100, 2);
        state.Memory.WriteByte(0x101, 3);
        state.Memory.WriteByte(0x150, 1);

        Assert.Throws<ZMachineRuntimeException>(() => CreateParser(state).Tokenise(0x100, 0x150, 0x400, false));
    }

    [Fact]
    public void RejectsUnterminatedV1ThroughV4TextBuffers()
    {
        var state = new ZMachineState(StoryLoader.Load(CreateDictionaryStory(3, ["go"], unsorted: false)));
        state.Memory.WriteByte(0x100, 1);
        state.Memory.WriteByte(0x101, (byte)'g');
        state.Memory.WriteByte(0x102, (byte)'o');
        state.Memory.WriteByte(0x103, (byte)'x');
        state.Memory.WriteByte(0x150, 0);

        Assert.Throws<ZMachineRuntimeException>(() => CreateParser(state).Tokenise(0x100, 0x150, 0x400, false));
    }

    private static ZMachineTextParser CreateParser(ZMachineState state) => new(
        state.Memory,
        state.Header,
        new ZMachineDictionary(state.Memory, state.Header, state.Characters),
        state.Text);

    private static byte[] CreateDictionaryStory(byte version, string[] words, bool unsorted, char[]? separators = null)
    {
        var story = MachineStoryFactory.Create(version);
        const int address = 0x400;
        WriteWord(story, 0x08, address);
        separators ??= [];
        story[address] = checked((byte)separators.Length);
        for (var index = 0; index < separators.Length; index++)
        {
            story[address + 1 + index] = checked((byte)separators[index]);
        }

        var entryLengthAddress = address + 1 + separators.Length;
        var keyLength = version <= 3 ? 4 : 6;
        var entryLength = keyLength + 3;
        story[entryLengthAddress] = checked((byte)entryLength);
        WriteWord(story, entryLengthAddress + 1, unsorted ? unchecked((ushort)-words.Length) : checked((ushort)words.Length));

        var orderedWords = words.ToArray();
        if (!unsorted)
        {
            Array.Sort(orderedWords, StringComparer.Ordinal);
        }

        var loaded = StoryLoader.Load(story);
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var entries = orderedWords.Select(word => codec.EncodeDictionaryText(System.Text.Encoding.ASCII.GetBytes(word))).ToArray();
        if (!unsorted)
        {
            Array.Sort(entries, CompareBytes);
        }

        var entryAddress = entryLengthAddress + 3;
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index].CopyTo(story, entryAddress + (index * entryLength));
        }

        return story;
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        for (var index = 0; index < left.Length; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static ushort DictionaryEntryAddress(byte version, int index, int separatorCount = 0) =>
        checked((ushort)(0x400 + 4 + separatorCount + (index * (version <= 3 ? 7 : 9))));

    private static void WriteTextBuffer(ZMachineMemory memory, ushort address, byte version, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        if (version <= 4)
        {
            memory.WriteByte(address, checked((byte)Math.Max(0, bytes.Length - 1)));
            for (var index = 0; index < bytes.Length; index++)
            {
                memory.WriteByte(checked((ushort)(address + 1 + index)), bytes[index]);
            }

            memory.WriteByte(checked((ushort)(address + 1 + bytes.Length)), 0);
        }
        else
        {
            memory.WriteByte(address, 255);
            memory.WriteByte(checked((ushort)(address + 1)), checked((byte)bytes.Length));
            for (var index = 0; index < bytes.Length; index++)
            {
                memory.WriteByte(checked((ushort)(address + 2 + index)), bytes[index]);
            }
        }
    }

    private static void AssertParseEntry(ZMachineMemory memory, ushort parseBuffer, int index, ushort address, byte length, byte position)
    {
        var entryAddress = checked((ushort)(parseBuffer + 2 + (index * 4)));
        Assert.Equal(address, memory.ReadWord(entryAddress));
        Assert.Equal(length, memory.ReadByte((ushort)(entryAddress + 2)));
        Assert.Equal(position, memory.ReadByte((ushort)(entryAddress + 3)));
    }

    private static void WriteWord(byte[] data, int address, ushort value)
    {
        data[address] = (byte)(value >> 8);
        data[address + 1] = (byte)value;
    }
}
