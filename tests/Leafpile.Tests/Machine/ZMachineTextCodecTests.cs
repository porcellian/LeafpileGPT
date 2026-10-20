using System.Buffers.Binary;
using System.Text;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Tests.Machine;

public sealed class ZMachineTextCodecTests
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
    public void DecodesDefaultAlphabetsForEveryVersion(byte version)
    {
        var story = MachineStoryFactory.Create(version);
        WriteText(story, 0x500, 6, 7, 8);
        var codec = CreateCodec(story);

        Assert.Equal("abc", codec.Decode(0x500));
    }

    [Fact]
    public void KeepsSingleCharacterShiftAcrossPackedWordBoundary()
    {
        var story = MachineStoryFactory.Create(3);
        WriteText(story, 0x500, 6, 7, 4, 6, 5, 5);

        Assert.Equal("abA", CreateCodec(story).Decode(0x500));
    }

    [Fact]
    public void HandlesVersion1NewlineAndVersion2ZsciiEscape()
    {
        var v1Story = MachineStoryFactory.Create(1);
        WriteText(v1Story, 0x500, 1, 0, 6);
        var v2Story = MachineStoryFactory.Create(2);
        WriteText(v2Story, 0x500, 5, 6, 4, 27, 5, 5);

        Assert.Equal("\n a", CreateCodec(v1Story).Decode(0x500));
        Assert.Equal("ä", CreateCodec(v2Story).Decode(0x500));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 8)]
    public void AppliesConsecutiveVersion1And2ShiftsFromTheCurrentAlphabet(byte version, int zcharacter)
    {
        var story = MachineStoryFactory.Create(version);
        WriteText(story, 0x500, 2, 2, zcharacter);

        Assert.Equal("0", CreateCodec(story).Decode(0x500));
    }

    [Theory]
    [InlineData(1, "0123456789.,!?_#'\"/\\<-:()")]
    [InlineData(2, "0123456789.,!?_#'\"/\\-:()")]
    public void EncodesAndDecodesEveryDefaultA2Character(byte version, string alphabet)
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(version));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var encoded = codec.EncodeZText(Encoding.ASCII.GetBytes(alphabet));
        var story = MachineStoryFactory.Create(version);
        encoded.CopyTo(story, 0x500);

        Assert.Equal(alphabet, CreateCodec(story).Decode(0x500));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ExpandsAbbreviationsAndRejectsNestedAbbreviations(byte version)
    {
        var story = MachineStoryFactory.Create(version);
        WriteWord(story, 0x100, 0x90);
        WriteText(story, 0x120, 13, 14, 5);
        WriteText(story, 0x500, 1, 0, 5);

        Assert.Equal("hi", CreateCodec(story).Decode(0x500));

        WriteText(story, 0x120, 1, 0, 5);
        var error = Assert.Throws<ZMachineRuntimeException>(() => CreateCodec(story).Decode(0x500));
        Assert.Contains("cannot contain another abbreviation", error.Message);

        WriteText(story, 0x120, 5, 6, 4);
        error = Assert.Throws<ZMachineRuntimeException>(() => CreateCodec(story).Decode(0x500));
        Assert.Contains("incomplete ZSCII escape", error.Message);
    }

    [Fact]
    public void SelectsAllThreeVersion3AbbreviationGroups()
    {
        var story = MachineStoryFactory.Create(3);
        WriteWord(story, 0x100, 0x90);
        WriteWord(story, 0x100 + (32 * sizeof(ushort)), 0x91);
        WriteWord(story, 0x100 + (64 * sizeof(ushort)), 0x92);
        WriteText(story, 0x120, 13, 14, 5);
        WriteText(story, 0x122, 15, 16, 5);
        WriteText(story, 0x124, 17, 18, 5);
        WriteText(story, 0x500, 1, 0, 2, 0, 3, 0);

        Assert.Equal("hijklm", CreateCodec(story).Decode(0x500));
    }

    [Fact]
    public void CustomAlphabetAndUnicodeTranslationTablesOverrideDefaults()
    {
        var story = MachineStoryFactory.Create(5);
        WriteWord(story, 0x34, 0x180);
        WriteWord(story, 0x36, 0x140);
        for (var index = 0; index < 78; index++)
        {
            story[0x180 + index] = (byte)'a';
        }

        story[0x180] = 155;
        story[0x180 + 26] = (byte)'Z';
        WriteWord(story, 0x140, 3);
        WriteWord(story, 0x142, 0);
        WriteWord(story, 0x144, 0);
        WriteWord(story, 0x146, 0x160);
        story[0x160] = 2;
        WriteWord(story, 0x161, 0x03a9);
        WriteWord(story, 0x163, 0x00e9);
        WriteText(story, 0x500, 6, 5, 5);
        WriteText(story, 0x502, 4, 6, 5);
        WriteText(story, 0x504, 5, 6, 4, 27, 5, 5);
        var loaded = StoryLoader.Load(story);
        var characters = new ZMachineCharacterSet(new ZMachineMemory(loaded), loaded.Header);
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header, characters);

        Assert.Equal("Ω", codec.Decode(0x500));
        Assert.Equal("Z", codec.Decode(0x502));
        Assert.Equal("Ω", codec.Decode(0x504));
        Assert.Equal("é", characters.ToUnicodeString(156));
        Assert.Equal("?", characters.ToUnicodeString(157));
        Assert.True(characters.TryGetZsciiForUnicode('Ω', out var zscii));
        Assert.Equal((byte)155, zscii);

        var replacementText = codec.EncodeZText(new byte[] { 8 });
        var replacementStory = MachineStoryFactory.Create(5);
        replacementText.CopyTo(replacementStory, 0x500);
        Assert.Equal("?", CreateCodec(replacementStory).Decode(0x500));
    }

    [Fact]
    public void UsesDefaultUnicodeTableAndValidatesUnicodeTableBoundsAndControlCharacters()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var characters = new ZMachineCharacterSet(new ZMachineMemory(loaded), loaded.Header);

        Assert.Equal("ä", characters.ToUnicodeString(155));
        Assert.Equal("¿", characters.ToUnicodeString(223));
        Assert.Equal("?", characters.ToUnicodeString(224));
        Assert.True(characters.IsDefinedForTextInput(155));
        Assert.False(characters.IsDefinedForTextInput(9));
        Assert.False(characters.IsDefinedForOutput(129));
        Assert.True(characters.IsDefinedForInput(254));
        Assert.False(characters.IsDefinedForInput(252));

        var v6Loaded = StoryLoader.Load(MachineStoryFactory.Create(6));
        var v6Characters = new ZMachineCharacterSet(new ZMachineMemory(v6Loaded), v6Loaded.Header);
        Assert.True(v6Characters.IsDefinedForInput(252));
        Assert.Equal("\t", v6Characters.ToUnicodeString(9));
        Assert.Equal("\u2002", v6Characters.ToUnicodeString(11));

        var controlCharacterStory = MachineStoryFactory.Create(5);
        WriteWord(controlCharacterStory, 0x36, 0x140);
        WriteWord(controlCharacterStory, 0x140, 3);
        WriteWord(controlCharacterStory, 0x146, 0x160);
        controlCharacterStory[0x160] = 1;
        WriteWord(controlCharacterStory, 0x161, 0x000a);
        var controlLoaded = StoryLoader.Load(controlCharacterStory);
        var controlMemory = new ZMachineMemory(controlLoaded);

        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineCharacterSet(controlMemory, controlLoaded.Header));

        var badBoundsStory = MachineStoryFactory.Create(5);
        WriteWord(badBoundsStory, 0x36, 0xffff);
        var badBoundsLoaded = StoryLoader.Load(badBoundsStory);

        Assert.Throws<ZMachineRuntimeException>(() => new ZMachineCharacterSet(new ZMachineMemory(badBoundsLoaded), badBoundsLoaded.Header));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 6)]
    [InlineData(8, 6)]
    public void EncodesDictionaryEntriesWithVersionLengthAndAsciiLowercasing(byte version, int expectedLength)
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(version));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var encoded = codec.EncodeDictionaryText("I"u8);

        Assert.Equal(expectedLength, encoded.Length);
        Assert.Equal("i", DecodeEncoded(version, encoded));
        Assert.Equal((ushort)0x38a5, ReadWord(encoded, 0));
        if (version <= 3)
        {
            Assert.Equal((ushort)0x94a5, ReadWord(encoded, 2));
        }
    }

    [Fact]
    public void UsesVersion1And2ShiftLocksAndKeepsEscapeValidWhenLockedToA2()
    {
        var v1 = StoryLoader.Load(MachineStoryFactory.Create(1));
        var v1Codec = new ZMachineTextCodec(new ZMachineMemory(v1), v1.Header);
        var v1Encoded = v1Codec.EncodeDictionaryText("!!"u8);
        Assert.Equal("!!", DecodeEncoded(1, v1Encoded));

        var v2 = StoryLoader.Load(MachineStoryFactory.Create(2));
        var v2Codec = new ZMachineTextCodec(new ZMachineMemory(v2), v2.Header);
        var v2Encoded = v2Codec.EncodeDictionaryText(new byte[] { (byte)'?', (byte)'?', 155 });

        Assert.Equal("??ä", DecodeEncoded(2, v2Encoded));
    }

    [Fact]
    public void LowercasesDefaultExtraCharactersWhenEncodingDictionaryText()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var encoded = codec.EncodeDictionaryText(new byte[] { 158 });

        Assert.Equal("ä", DecodeEncoded(5, encoded));
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
    public void EncodesAndDecodesPackedTextWithShiftsAndEscapes(byte version)
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(version));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var source = new byte[] { (byte)'H', (byte)'i', (byte)',', (byte)' ', 155, (byte)'!' };
        var packed = codec.EncodeZText(source);
        var story = MachineStoryFactory.Create(version);
        packed.CopyTo(story, 0x500);

        Assert.Equal(version == 1 ? "Hi, ?!" : "Hi, ä!", CreateCodec(story).Decode(0x500));
    }

    [Fact]
    public void TruncatesOnlyAtDictionaryZcharacterCapacityAndKeepsIncompleteEscapePrefix()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(4));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
        var encoded = codec.EncodeDictionaryText("abcdefä"u8);
        var story = MachineStoryFactory.Create(4);
        encoded.CopyTo(story, 0x500);

        Assert.Equal(6, encoded.Length);
        Assert.Equal("abcdef", CreateCodec(story).Decode(0x500));
    }

    [Fact]
    public void RejectsTextAndDictionaryRangesOutsideMemoryOrSourceBuffers()
    {
        var loaded = StoryLoader.Load(MachineStoryFactory.Create(5));
        var codec = new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);

        Assert.Throws<ZMachineRuntimeException>(() => codec.Decode(loaded.Header.FileLength - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.EncodeDictionaryText([1, 2, 3], 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.EncodeDictionaryText([1, 2, 3], -1, 1));
    }

    [Fact]
    public void RestartRecreatesTextServicesAgainstResetMemory()
    {
        var story = MachineStoryFactory.Create(5);
        WriteText(story, 0x200, 6, 5, 5);
        var state = new ZMachineState(StoryLoader.Load(story));
        WriteTextInMemory(state.Memory, 0x200, 7, 5, 5);

        Assert.Equal("b", state.Text.Decode(0x200));

        state.Restart();

        Assert.Equal("a", state.Text.Decode(0x200));
    }

    private static string DecodeEncoded(byte version, byte[] encoded)
    {
        var story = MachineStoryFactory.Create(version);
        encoded.CopyTo(story, 0x500);
        return CreateCodec(story).Decode(0x500);
    }

    private static ZMachineTextCodec CreateCodec(byte[] story)
    {
        var loaded = StoryLoader.Load(story);
        return new ZMachineTextCodec(new ZMachineMemory(loaded), loaded.Header);
    }

    private static void WriteText(byte[] story, int address, params int[] zcharacters)
    {
        var padded = zcharacters.ToList();
        while (padded.Count % 3 != 0)
        {
            padded.Add(5);
        }

        for (var wordIndex = 0; wordIndex < padded.Count / 3; wordIndex++)
        {
            var index = wordIndex * 3;
            var word = (ushort)((padded[index] << 10) | (padded[index + 1] << 5) | padded[index + 2]);
            if (wordIndex == padded.Count / 3 - 1)
            {
                word |= 0x8000;
            }

            WriteWord(story, address + (wordIndex * sizeof(ushort)), word);
        }
    }

    private static void WriteTextInMemory(ZMachineMemory memory, ushort address, params int[] zcharacters)
    {
        var padded = zcharacters.ToList();
        while (padded.Count % 3 != 0)
        {
            padded.Add(5);
        }

        for (var wordIndex = 0; wordIndex < padded.Count / 3; wordIndex++)
        {
            var index = wordIndex * 3;
            var word = (ushort)((padded[index] << 10) | (padded[index + 1] << 5) | padded[index + 2]);
            if (wordIndex == padded.Count / 3 - 1)
            {
                word |= 0x8000;
            }

            memory.WriteWord((ushort)(address + (wordIndex * sizeof(ushort))), word);
        }
    }

    private static ushort ReadWord(byte[] data, int address) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(address, sizeof(ushort)));

    private static void WriteWord(byte[] data, int address, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(address, sizeof(ushort)), value);
}
