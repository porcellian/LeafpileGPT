namespace LeafpileGPT.Tests;

using System.Buffers.Binary;
using LeafpileGPT.Core;
using Xunit;

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
    public void DecodesDefaultLowercaseAlphabetInEveryVersion(byte version)
    {
        var codec = CreateCodec(version);

        Assert.Equal("abc", codec.DecodePackedText(PackZCharacters(6, 7, 8)));
    }

    [Theory]
    [InlineData(1, 2, 6, "A")]
    [InlineData(1, 3, 7, "0")]
    [InlineData(2, 2, 6, "A")]
    [InlineData(2, 3, 8, "0")]
    [InlineData(3, 4, 6, "A")]
    [InlineData(3, 5, 8, "0")]
    [InlineData(4, 4, 6, "A")]
    [InlineData(4, 5, 8, "0")]
    [InlineData(5, 4, 6, "A")]
    [InlineData(5, 5, 8, "0")]
    [InlineData(6, 4, 6, "A")]
    [InlineData(6, 5, 8, "0")]
    [InlineData(7, 4, 6, "A")]
    [InlineData(7, 5, 8, "0")]
    [InlineData(8, 4, 6, "A")]
    [InlineData(8, 5, 8, "0")]
    public void AppliesVersionSpecificTemporaryShifts(byte version, byte shift, byte zCharacter, string expected)
    {
        var codec = CreateCodec(version);

        Assert.Equal(expected, codec.DecodeZCharacters([shift, zCharacter]));
    }

    [Fact]
    public void VersionOneSupportsNewlineAndShiftLocks()
    {
        var codec = CreateCodec(1);
        Assert.Equal("\n", codec.DecodeZCharacters([1]));
        Assert.Equal("A0", codec.DecodeZCharacters([4, 6, 4, 7]));
    }

    [Fact]
    public void VersionTwoShiftLocksPersistUntilChanged()
    {
        var codec = CreateCodec(2);

        Assert.Equal("ABC", codec.DecodePackedText(PackZCharacters(4, 6, 7, 8)));
        Assert.Equal("Aabc", codec.DecodePackedText(PackZCharacters(4, 6, 5, 6, 7, 8)));
    }

    [Fact]
    public void VersionThreeAndLaterUseAbbreviationsAndEscapeCharacters()
    {
        var codec = CreateCodec(5);
        var zsciiEscape = PackZCharacters(5, 6, 5, 10); // U+00E9, ZSCII 170.

        Assert.Equal("é", codec.DecodePackedText(zsciiEscape));
    }

    [Fact]
    public void VersionOneA2AlphabetUsesItsDifferentPunctuationRow()
    {
        var codec = CreateCodec(1);

        Assert.Equal("0<", codec.DecodePackedText(PackZCharacters(5, 7, 27)));
    }

    [Fact]
    public void ExpandsVersionTwoAndVersionThreeAbbreviationsFromWordAddresses()
    {
        foreach (var (version, marker, suffix) in new[] { ((byte)2, (byte)1, (byte)0), ((byte)3, (byte)3, (byte)31) })
        {
            var codec = CreateCodec(version, bytes =>
            {
                StoryFixtureBuilder.WriteWord(bytes, 0x18, 300);
                var entry = version == 2 ? 0 : 95;
                StoryFixtureBuilder.WriteWord(bytes, 300 + entry * 2, 200);
                WritePackedString(bytes, 400, 25, 13, 10); // "the"
            });

            Assert.Equal("thea", codec.DecodePackedText(PackZCharacters(marker, suffix, 6)));
        }
    }

    [Fact]
    public void IgnoresAnAbbreviationMarkerInsideAnAbbreviationWithoutRecursion()
    {
        var codec = CreateCodec(3, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x18, 300);
            StoryFixtureBuilder.WriteWord(bytes, 300, 200);
            WritePackedString(bytes, 400, 1, 0, 13); // Nested abbreviation marker followed by "h".
        });

        Assert.Equal("ha", codec.DecodePackedText(PackZCharacters(1, 0, 6)));
    }

    [Fact]
    public void RejectsAbbreviationUseWithoutATableAndWithAnInvalidEntryAddress()
    {
        var absentTable = CreateCodec(3);
        Assert.Throws<ZMachineTextFormatException>(() => absentTable.DecodeZCharacters([1, 0, 6]));

        var invalidTable = CreateCodec(3, bytes => StoryFixtureBuilder.WriteWord(bytes, 0x18, 1023));
        Assert.Throws<ZMachineTextFormatException>(() => invalidTable.DecodeZCharacters([1, 0, 6]));
    }

    [Fact]
    public void DecodesPackedStringsAtByteAndPackedAddresses()
    {
        var v1Codec = CreateCodec(1, bytes => WritePackedString(bytes, 400, 6, 7, 8));
        var v5Codec = CreateCodec(5, bytes => WritePackedString(bytes, 400, 6, 7, 8));
        var v8Codec = CreateCodec(8, bytes => WritePackedString(bytes, 400, 6, 7, 8));

        Assert.Equal("abc", v1Codec.DecodeStringAtByteAddress(400));
        Assert.Equal("abc", v1Codec.DecodePackedString(200));
        Assert.Equal("abc", v5Codec.DecodePackedString(100));
        Assert.Equal("abc", v8Codec.DecodePackedString(50));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public void DecodesV6AndV7PackedStringsUsingTheStringsOffset(byte version)
    {
        var codec = CreateCodec(version, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x2A, 10);
            WritePackedString(bytes, 480, 6, 7, 8);
        });

        Assert.Equal("abc", codec.DecodePackedString(100));
    }

    [Fact]
    public void IgnoresIncompleteEscapeAndAbbreviationAtEndOfZCharacterStream()
    {
        var codec = CreateCodec(5);

        Assert.Equal(string.Empty, codec.DecodeZCharacters([5, 6, 4]));
        Assert.Equal(string.Empty, codec.DecodeZCharacters([1]));
    }

    [Fact]
    public void RejectsMalformedPackedTextAndInvalidZCharacters()
    {
        var codec = CreateCodec(5);

        Assert.Throws<ZMachineTextFormatException>(() => codec.DecodePackedText([0, 1]));
        Assert.Throws<ZMachineTextFormatException>(() => codec.DecodePackedText([0]));
        Assert.Throws<ZMachineTextFormatException>(() => codec.DecodeZCharacters([32]));
        Assert.Throws<ZMachineTextFormatException>(() => codec.DecodeStringAtByteAddress(1023));
    }

    [Fact]
    public void LoadsCustomAlphabetTableForVersionFiveAndLater()
    {
        var codec = CreateCodec(5, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x34, 500);
            bytes[500] = 155; // Z-character 6 in A0 maps to ZSCII 155.
        });

        Assert.Equal("ä", codec.DecodeZCharacters([6]));
    }

    [Fact]
    public void RejectsCustomAlphabetTableOutsideTheStoryImage()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        StoryFixtureBuilder.WriteWord(bytes, 0x34, 1023);
        UpdateChecksum(bytes);

        Assert.Throws<ZMachineTextFormatException>(() => new ZMachineTextCodec(
            new ZMachineMemory(StoryFile.Parse(bytes))));
    }

    [Fact]
    public void LoadsUnicodeTranslationTableFromThirdHeaderExtensionWord()
    {
        var codec = CreateCodec(5, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x36, 450);
            StoryFixtureBuilder.WriteWord(bytes, 450, 3);
            StoryFixtureBuilder.WriteWord(bytes, 456, 500);
            bytes[500] = 1;
            StoryFixtureBuilder.WriteWord(bytes, 501, 0x2603);
        });

        Assert.Equal('☃', codec.Zscii.Decode(155));
        Assert.True(codec.Zscii.TryEncodeInput('☃', out var code));
        Assert.Equal((byte)155, code);
        Assert.Equal("☃", codec.DecodeZCharacters([5, 6, 4, 27]));
    }

    [Fact]
    public void MissingUnicodeTableUsesTheStandardDefaultAndInvalidCodesAreSafe()
    {
        var codec = CreateCodec(5);

        Assert.Equal('ä', codec.Zscii.Decode(155));
        Assert.Equal('¿', codec.Zscii.Decode(223));
        Assert.Equal('\uFFFD', codec.Zscii.Decode(224));
        Assert.Equal('\uFFFD', codec.Zscii.Decode(0x123));
        Assert.False(codec.Zscii.TryEncodeInput('\u007F', out _));
        Assert.True(codec.Zscii.IsDefinedInputCode(8));
        Assert.False(codec.Zscii.IsDefinedInputCode(127));
    }

    [Fact]
    public void VersionSixMapsOutputOnlyTabAndSentenceSpace()
    {
        var codec = CreateCodec(6);

        Assert.Equal('\t', codec.Zscii.Decode(9));
        Assert.Equal('\u2002', codec.Zscii.Decode(11));
        Assert.Equal('\uFFFD', CreateCodec(5).Zscii.Decode(9));
    }

    [Fact]
    public void ZsciiInputCapabilitiesFollowVersionRules()
    {
        var v4 = CreateCodec(4).Zscii;
        var v5 = CreateCodec(5).Zscii;
        var v6 = CreateCodec(6).Zscii;

        Assert.False(v4.IsDefinedInputCode(253));
        Assert.True(v5.IsDefinedInputCode(254));
        Assert.True(v6.IsDefinedInputCode(252));
        Assert.True(v6.IsDefinedInputCode(253));
    }

    [Fact]
    public void RejectsInvalidUnicodeTableSizeAndHandlesInvalidCodePointsAsUndefined()
    {
        Assert.Throws<ZMachineTextFormatException>(() => CreateCodec(5, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x36, 450);
            StoryFixtureBuilder.WriteWord(bytes, 450, 3);
            StoryFixtureBuilder.WriteWord(bytes, 456, 500);
            bytes[500] = 98;
        }));

        var invalidPoint = CreateCodec(5, bytes =>
        {
            StoryFixtureBuilder.WriteWord(bytes, 0x36, 450);
            StoryFixtureBuilder.WriteWord(bytes, 450, 3);
            StoryFixtureBuilder.WriteWord(bytes, 456, 500);
            bytes[500] = 1;
            StoryFixtureBuilder.WriteWord(bytes, 501, 0xD800);
        });
        Assert.Equal('\uFFFD', invalidPoint.Zscii.Decode(155));
        Assert.False(invalidPoint.Zscii.IsDefinedInputCode(155));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(4, 9)]
    [InlineData(5, 9)]
    [InlineData(6, 9)]
    [InlineData(7, 9)]
    [InlineData(8, 9)]
    public void EncodesDictionaryWordsToVersionSpecificFixedWidths(byte version, int expectedZCharacterCount)
    {
        var codec = CreateCodec(version);
        var encoded = codec.EncodeDictionaryWord("i");
        var expectedWords = expectedZCharacterCount / 3;

        Assert.Equal(expectedWords * sizeof(ushort), encoded.Length);
        Assert.Equal((byte)0x38, encoded[0]);
        Assert.Equal((byte)0xA5, encoded[1]);
        Assert.Equal((byte)0x80, (byte)(encoded[^2] & 0x80));
        Assert.Equal("i", codec.DecodePackedText(encoded.AsSpan()));
    }

    [Fact]
    public void DictionaryEncodingUsesLowercaseAndA2EscapeWhenSpaceAllows()
    {
        var codec = CreateCodec(5);
        var encoded = codec.EncodeDictionaryWord("É");

        Assert.Equal("é", codec.DecodePackedText(encoded.AsSpan()));
        var leadingSpace = codec.EncodeDictionaryWord(" a");
        Assert.Equal((byte)0, leadingSpace[0]);
        Assert.Equal((byte)0xC5, leadingSpace[1]);
        Assert.Equal(" a", codec.DecodePackedText(leadingSpace.AsSpan()));
        Assert.Equal("éé", codec.DecodePackedText(codec.EncodeDictionaryWord("ééé").AsSpan()));
    }

    [Fact]
    public void DictionaryEncodingFallsBackSafelyForUnrepresentableCharacters()
    {
        var codec = CreateCodec(1);
        var encoded = codec.EncodeDictionaryWord("☃");

        Assert.Equal("?", codec.DecodePackedText(encoded.AsSpan()));
    }

    [Fact]
    public void HeaderExposesAbbreviationsTableAddress()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(5));

        Assert.Equal((ushort)0, story.Header.AbbreviationsTableAddress);
    }

    private static ZMachineTextCodec CreateCodec(byte version, Action<byte[]>? configure = null)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        configure?.Invoke(bytes);
        UpdateChecksum(bytes);
        return new ZMachineTextCodec(new ZMachineMemory(StoryFile.Parse(bytes)));
    }

    private static void WritePackedString(byte[] story, int address, params byte[] zCharacters)
    {
        var packed = PackZCharacters(zCharacters);
        packed.CopyTo(story, address);
    }

    private static byte[] PackZCharacters(params byte[] zCharacters)
    {
        var fullLength = ((zCharacters.Length + 2) / 3) * 3;
        var padded = Enumerable.Repeat((byte)5, fullLength).ToArray();
        zCharacters.CopyTo(padded, 0);
        var bytes = new byte[(fullLength / 3) * sizeof(ushort)];
        for (var offset = 0; offset < fullLength; offset += 3)
        {
            var word = (ushort)((padded[offset] << 10) | (padded[offset + 1] << 5) | padded[offset + 2]);
            if (offset + 3 == fullLength)
            {
                word |= 0x8000;
            }

            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset / 3 * sizeof(ushort)), word);
        }

        return bytes;
    }

    private static void UpdateChecksum(byte[] story)
    {
        if (story[0] >= 3)
        {
            StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));
        }
    }
}
