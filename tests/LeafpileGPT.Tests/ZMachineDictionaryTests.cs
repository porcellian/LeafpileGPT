namespace LeafpileGPT.Tests;

using System.Collections.Immutable;
using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineDictionaryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void DictionaryLookupUsesVersionSpecificEncodedWordWidths(byte version)
    {
        var (story, codec, dictionaryAddress) = CreateDictionary(version, ["go", "look", "take"], sorted: true);
        var memory = new ZMachineMemory(story);
        var dictionary = new ZMachineDictionary(memory, codec);

        Assert.Equal((byte)(version <= 3 ? 7 : 9), dictionary.EntryLength);
        Assert.Equal((ushort)200, dictionaryAddress);
        Assert.NotEqual((ushort)0, dictionary.FindWord("look"u8));
        Assert.Equal((ushort)0, dictionary.FindWord("xyzzy"u8));
    }

    [Fact]
    public void SearchesUnsortedUserDictionaryLinearly()
    {
        var (story, codec, _) = CreateDictionary(5, ["take", "go", "look"], sorted: false);
        var memory = new ZMachineMemory(story);
        var dictionary = new ZMachineDictionary(memory, codec);

        Assert.NotEqual((ushort)0, dictionary.FindWord("go"u8));
        Assert.NotEqual((ushort)0, dictionary.FindWord("take"u8));
    }

    [Theory]
    [InlineData(3, 1, 5, 7)]
    [InlineData(5, 2, 6, 8)]
    public void TokeniseSplitsSpacesAndSeparatorsAndWritesParseRecords(byte version, byte firstPosition, byte separatorPosition,
        byte finalPosition)
    {
        var (story, codec, _) = CreateDictionary(version, ["go", "look", "take"], sorted: true);
        var memory = new ZMachineMemory(story);
        WriteInputText(memory, version, 400, "look, grue");
        memory.WriteByte(500, 5);
        var dictionary = new ZMachineDictionary(memory, codec);

        dictionary.Tokenise(400, 500);

        Assert.Equal((byte)3, memory.ReadByte(501));
        Assert.NotEqual((ushort)0, memory.ReadWord(502));
        Assert.Equal((byte)4, memory.ReadByte(504));
        Assert.Equal(firstPosition, memory.ReadByte(505));
        Assert.Equal((ushort)0, memory.ReadWord(506));
        Assert.Equal((byte)1, memory.ReadByte(508));
        Assert.Equal(separatorPosition, memory.ReadByte(509));
        Assert.Equal((ushort)0, memory.ReadWord(510));
        Assert.Equal((byte)4, memory.ReadByte(512));
        Assert.Equal(finalPosition, memory.ReadByte(513));
    }

    [Fact]
    public void TokeniseHonorsWordCapacityAndPreservesUnknownSlotsWhenFlagIsSet()
    {
        var (story, codec, _) = CreateDictionary(5, ["look"], sorted: true);
        var memory = new ZMachineMemory(story);
        WriteInputText(memory, 5, 400, "look grue take");
        memory.WriteByte(500, 2);
        memory.WriteWord(506, 0xBEEF);
        memory.WriteByte(508, 0xAA);
        memory.WriteByte(509, 0xBB);
        var dictionary = new ZMachineDictionary(memory, codec);

        dictionary.Tokenise(400, 500, skipUnrecognizedWords: true);

        Assert.Equal((byte)2, memory.ReadByte(501));
        Assert.NotEqual((ushort)0, memory.ReadWord(502));
        Assert.Equal((ushort)0xBEEF, memory.ReadWord(506));
        Assert.Equal((byte)0xAA, memory.ReadByte(508));
        Assert.Equal((byte)0xBB, memory.ReadByte(509));
    }

    [Fact]
    public void EncodeTextWritesFixedWidthDictionaryFormFromAnArrayOffset()
    {
        var (story, codec, _) = CreateDictionary(5, ["look"], sorted: true);
        var memory = new ZMachineMemory(story);
        foreach (var (index, character) in "XLOOK".Select((character, index) => (index, character)))
        {
            memory.WriteByte(checked((ushort)(400 + index)), checked((byte)character));
        }
        var dictionary = new ZMachineDictionary(memory, codec);

        dictionary.EncodeText(400, 4, 1, 550);

        Assert.Equal(codec.EncodeDictionaryWord("look").ToArray(),
            Enumerable.Range(0, 6).Select(index => memory.ReadByte((ushort)(550 + index))));
    }

    [Fact]
    public void RejectsMalformedDictionaryHeadersAndParseRanges()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        bytes[200] = 1;
        bytes[201] = 32; // Space cannot be a word separator.
        bytes[202] = 6;
        StoryFixtureBuilder.WriteWord(bytes, 203, 0);
        UpdateChecksum(bytes);
        var memory = new ZMachineMemory(StoryFile.Parse(bytes));

        Assert.Throws<ZMachineExecutionException>(() => new ZMachineDictionary(memory).FindWord("go"u8));
        Assert.Throws<ZMachineExecutionException>(() => new ZMachineDictionary(memory).Tokenise(400, 0));
    }

    [Fact]
    public void ExecutesTokeniseAndEncodeTextOpcodes()
    {
        var (story, _, _) = CreateDictionary(5, ["look"], sorted: true);
        var bytes = story.Bytes.ToArray();
        WriteInputText(bytes, 5, 400, "look");
        bytes[500] = 2;
        bytes[540] = (byte)'L';
        bytes[541] = (byte)'O';
        bytes[542] = (byte)'O';
        bytes[543] = (byte)'K';
        UpdateChecksum(bytes);
        var state = ZMachineExecutionState.Start(StoryFile.Parse(bytes));
        var executor = new ZMachineOpcodeExecutor();

        executor.Execute(Instruction("tokenise", [Constant(400), Constant(500)]), state);
        executor.Execute(Instruction("encode_text", [Constant(540), Constant(4), Constant(0), Constant(550)]), state);

        Assert.Equal((byte)1, state.Memory.ReadByte(501));
        var expectedDictionaryAddress = new ZMachineDictionary(state.Memory).FindWord("look"u8);
        Assert.Equal(expectedDictionaryAddress, state.Memory.ReadWord(502));
        Assert.Equal((byte)4, state.Memory.ReadByte(504));
        Assert.Equal((byte)2, state.Memory.ReadByte(505));
        Assert.Equal(new ZMachineTextCodec(state.Memory).EncodeDictionaryWord("look").ToArray(),
            Enumerable.Range(0, 6).Select(index => state.Memory.ReadByte((ushort)(550 + index))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StoresLegacyInputLowercaseWithZeroTerminator(byte version)
    {
        var memory = CreateMemory(version);
        memory.WriteByte(400, 3); // Maximum is the stored value plus one in V1–V4.
        var codec = new ZMachineTextCodec(memory);

        var terminator = ZMachineTextBuffer.StoreInput(memory, codec.Zscii, 400, "HELLO");

        Assert.Equal((byte)13, terminator);
        Assert.Equal("hell", DecodeInput(memory, 400));
        Assert.Equal((byte)0, memory.ReadByte(405));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void AppendsToExistingV5PlusTextAndReturnsTerminator(byte version)
    {
        var memory = CreateMemory(version);
        memory.WriteByte(400, 8);
        memory.WriteByte(401, 2);
        memory.WriteByte(402, (byte)'h');
        memory.WriteByte(403, (byte)'i');
        var codec = new ZMachineTextCodec(memory);

        var terminator = ZMachineTextBuffer.StoreInput(memory, codec.Zscii, 400, "THERE", 10);

        Assert.Equal((byte)13, terminator);
        Assert.Equal((byte)7, memory.ReadByte(401));
        Assert.Equal("hithere", DecodeInput(memory, 400));
        Assert.Equal((byte)0, ZMachineTextBuffer.StoreInput(memory, codec.Zscii, 400, [], 0));
    }

    [Fact]
    public void RejectsInvalidInputWithoutPartiallyChangingTheBuffer()
    {
        var memory = CreateMemory(5);
        memory.WriteByte(400, 8);
        memory.WriteByte(401, 0);
        memory.WriteByte(402, 0);
        var codec = new ZMachineTextCodec(memory);

        Assert.Throws<ZMachineExecutionException>(() => ZMachineTextBuffer.StoreInput(memory, codec.Zscii, 400, "a😀"));

        Assert.Equal((byte)0, memory.ReadByte(401));
        Assert.Equal((byte)0, memory.ReadByte(402));
    }

    private static (StoryFile Story, ZMachineTextCodec Codec, ushort DictionaryAddress) CreateDictionary(
        byte version, string[] words, bool sorted, byte[]? separators = null)
    {
        separators ??= [44];
        var bytes = StoryFixtureBuilder.Create(version);
        var dictionaryAddress = (ushort)200;
        var encodedWords = words.Select(word => (Word: word, Encoded: Encode(bytes, version, word))).ToArray();
        if (sorted)
        {
            Array.Sort(encodedWords, (left, right) => left.Encoded.AsSpan().SequenceCompareTo(right.Encoded));
        }

        bytes[dictionaryAddress] = checked((byte)separators.Length);
        for (var index = 0; index < separators.Length; index++)
        {
            bytes[dictionaryAddress + 1 + index] = separators[index];
        }

        var entryLength = checked((byte)((version <= 3 ? 4 : 6) + 3));
        var entryLengthAddress = dictionaryAddress + 1 + separators.Length;
        bytes[entryLengthAddress] = entryLength;
        StoryFixtureBuilder.WriteWord(bytes, entryLengthAddress + 1, sorted ? (ushort)words.Length : unchecked((ushort)-words.Length));
        var entriesAddress = entryLengthAddress + 3;
        foreach (var item in encodedWords.Select((item, index) => (item, index)))
        {
            var entryAddress = entriesAddress + item.index * entryLength;
            Array.Copy(item.item.Encoded, 0, bytes, entryAddress, item.item.Encoded.Length);
            for (var index = item.item.Encoded.Length; index < entryLength; index++)
            {
                bytes[entryAddress + index] = (byte)(index - item.item.Encoded.Length + 1);
            }
        }

        UpdateChecksum(bytes);
        var story = StoryFile.Parse(bytes);
        var memory = new ZMachineMemory(story);
        return (story, new ZMachineTextCodec(memory), dictionaryAddress);
    }

    private static byte[] Encode(byte[] story, byte version, string word)
    {
        var memory = new ZMachineMemory(StoryFile.Parse(story));
        return new ZMachineTextCodec(memory).EncodeDictionaryWord(word).ToArray();
    }

    private static ZMachineMemory CreateMemory(byte version)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        UpdateChecksum(bytes);
        return new ZMachineMemory(StoryFile.Parse(bytes));
    }

    private static string DecodeInput(ZMachineMemory memory, ushort address) =>
        string.Concat(ZMachineTextBuffer.ReadText(memory, address).Select(memoryByte => (char)memoryByte));

    private static void WriteInputText(ZMachineMemory memory, byte version, ushort address, string text)
    {
        WriteInputText(memory, version, address, text, memory.WriteByte);
    }

    private static void WriteInputText(byte[] memory, byte version, ushort address, string text)
    {
        var chars = text.Select(character => checked((byte)character)).ToArray();
        if (version <= 4)
        {
            memory[address] = checked((byte)Math.Max(0, chars.Length - 1));
            for (var index = 0; index < chars.Length; index++)
            {
                memory[address + 1 + index] = (byte)char.ToLowerInvariant((char)chars[index]);
            }

            memory[address + 1 + chars.Length] = 0;
        }
        else
        {
            memory[address] = 80;
            memory[address + 1] = checked((byte)chars.Length);
            for (var index = 0; index < chars.Length; index++)
            {
                memory[address + 2 + index] = (byte)char.ToLowerInvariant((char)chars[index]);
            }
        }
    }

    private static void WriteInputText(ZMachineMemory memory, byte version, ushort address, string text,
        Action<ushort, byte> writeByte)
    {
        var chars = text.Select(character => checked((byte)character)).ToArray();
        if (version <= 4)
        {
            writeByte(address, checked((byte)Math.Max(0, chars.Length - 1)));
            for (var index = 0; index < chars.Length; index++)
            {
                writeByte(checked((ushort)(address + 1 + index)), (byte)char.ToLowerInvariant((char)chars[index]));
            }

            writeByte(checked((ushort)(address + 1 + chars.Length)), 0);
        }
        else
        {
            writeByte(address, 80);
            writeByte(checked((ushort)(address + 1)), checked((byte)chars.Length));
            for (var index = 0; index < chars.Length; index++)
            {
                writeByte(checked((ushort)(address + 2 + index)), (byte)char.ToLowerInvariant((char)chars[index]));
            }
        }
    }

    private static DecodedInstruction Instruction(string mnemonic, DecodedOperand[] operands)
    {
        var definition = new OpcodeDefinition(OpcodeClass.Variable, 0, mnemonic, 5, 8,
            checked((byte)operands.Length), checked((byte)operands.Length));
        return new DecodedInstruction(850, 851, InstructionForm.Variable, OpcodeClass.Variable, 0,
            definition, operands.ToImmutableArray(), null, null, ImmutableArray<byte>.Empty);
    }

    private static DecodedOperand Constant(ushort value) => new(OperandEncoding.SmallConstant, value);

    private static void UpdateChecksum(byte[] bytes) =>
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
}
