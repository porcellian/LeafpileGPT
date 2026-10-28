using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public class StoryFileTests
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
    public void ParsesEveryStandardVersion(byte version)
    {
        var data = StoryFixtureBuilder.Create(version);

        var story = StoryFile.Parse(data);

        Assert.Equal(version, story.Header.Version);
        Assert.Equal(StoryFixtureBuilder.DefaultLength, story.Length);
        Assert.Equal(42, story.Header.ReleaseNumber);
        Assert.Equal("840102", story.Header.SerialNumber);
        Assert.Equal(StoryFixtureBuilder.DefaultStaticBase, story.Header.StaticMemoryBase);
        Assert.Equal(version == 6, story.Header.UsesPackedMainRoutine);
        if (version >= 3)
        {
            Assert.Equal(StoryChecksumStatus.Valid, story.Header.ChecksumStatus);
            Assert.Equal(StoryFixtureBuilder.CalculateChecksum(data), story.Header.CalculatedChecksum);
        }
        else
        {
            Assert.Equal(StoryChecksumStatus.NotDefined, story.Header.ChecksumStatus);
            Assert.Null(story.Header.StoredChecksum);
            Assert.Null(story.Header.FileLengthWord);
            Assert.Null(story.Header.RoutinesOffset);
        }
    }

    [Fact]
    public void AcceptsLegacyVersionThreeWithoutLengthOrChecksum()
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(3, includeLengthAndChecksum: false));

        Assert.Equal(StoryFixtureBuilder.DefaultLength, story.Length);
        Assert.Equal(StoryChecksumStatus.UnavailableInLegacyVersion3, story.Header.ChecksumStatus);
        Assert.Null(story.Header.CalculatedChecksum);
    }

    [Fact]
    public void ParsesVersionSixInterpreterDisplayAndAddressFields()
    {
        var data = StoryFixtureBuilder.Create(6);
        data[0x1E] = 7;
        data[0x1F] = (byte)'A';
        data[0x20] = 25;
        data[0x21] = 80;
        data[0x26] = 16;
        data[0x27] = 8;
        StoryFixtureBuilder.WriteWord(data, 0x22, 640);
        StoryFixtureBuilder.WriteWord(data, 0x24, 400);
        StoryFixtureBuilder.WriteWord(data, 0x28, 5);
        StoryFixtureBuilder.WriteWord(data, 0x2A, 9);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));

        var header = StoryFile.Parse(data).Header;

        Assert.Equal((byte)7, header.InterpreterNumber!.Value);
        Assert.Equal((byte)'A', header.InterpreterVersion!.Value);
        Assert.Equal((byte)25, header.ScreenHeightInLines!.Value);
        Assert.Equal((byte)80, header.ScreenWidthInCharacters!.Value);
        Assert.Equal((ushort)640, header.ScreenWidthInUnits!.Value);
        Assert.Equal((ushort)400, header.ScreenHeightInUnits!.Value);
        Assert.Equal((byte)8, header.FontWidthInUnits!.Value);
        Assert.Equal((byte)16, header.FontHeightInUnits!.Value);
        Assert.Equal((ushort)5, header.RoutinesOffset!.Value);
        Assert.Equal((ushort)9, header.StringsOffset!.Value);
    }

    [Fact]
    public void OmitsLaterVersionHeaderFieldsForVersionThree()
    {
        var header = StoryFile.Parse(StoryFixtureBuilder.Create(3)).Header;

        Assert.Null(header.InterpreterNumber);
        Assert.Null(header.ScreenHeightInLines);
        Assert.Null(header.ScreenWidthInUnits);
        Assert.Null(header.FontWidthInUnits);
        Assert.Null(header.TerminatingCharactersTableAddress);
    }

    [Fact]
    public void TrimsTrailingPaddingAndExcludesItFromChecksum()
    {
        var data = StoryFixtureBuilder.Create(5);
        Array.Resize(ref data, data.Length + 3);
        data[^3] = 0xA1;
        data[^2] = 0xB2;
        data[^1] = 0xC3;

        var story = StoryFile.Parse(data);

        Assert.Equal(StoryFixtureBuilder.DefaultLength, story.Length);
        Assert.Equal(StoryChecksumStatus.Valid, story.Header.ChecksumStatus);
        Assert.Equal(StoryFixtureBuilder.CalculateChecksum(data.AsSpan(0, StoryFixtureBuilder.DefaultLength)), story.Header.CalculatedChecksum);
    }

    [Fact]
    public void RejectsChecksumMismatchWithExpectedAndActualValues()
    {
        var data = StoryFixtureBuilder.Create(5);
        var expected = (ushort)(ReadWord(data, 0x1C) + 1);
        StoryFixtureBuilder.WriteWord(data, 0x1C, expected);

        var exception = Assert.Throws<StoryFileChecksumException>(() => StoryFile.Parse(data));

        Assert.Equal(expected, exception.ExpectedChecksum);
        Assert.Equal(StoryFixtureBuilder.CalculateChecksum(data), exception.ActualChecksum);
        Assert.Contains("checksum mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsTruncatedHeader()
    {
        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(new byte[63]));

        Assert.Contains("header requires 64 bytes", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsUnsupportedVersions(byte version)
    {
        var data = StoryFixtureBuilder.Create(3);
        data[0] = version;

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("expected a version from 1 through 8", exception.Message);
    }

    [Fact]
    public void RejectsDeclaredLengthShorterThanHeader()
    {
        var data = StoryFixtureBuilder.Create(4);
        StoryFixtureBuilder.WriteWord(data, 0x1A, 15);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("shorter than the 64-byte header", exception.Message);
    }

    [Fact]
    public void RejectsDeclaredLengthBeyondAvailableData()
    {
        var data = StoryFixtureBuilder.Create(5);
        var truncated = data[..800];

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(truncated));

        Assert.Contains("declares 1024 bytes", exception.Message);
        Assert.Contains("contains only 800 bytes", exception.Message);
    }

    [Theory]
    [InlineData(1, 128 * 1024 + 1)]
    [InlineData(2, 128 * 1024 + 1)]
    [InlineData(3, 128 * 1024 + 1)]
    public void RejectsPhysicalFilesAboveVersionMemoryLimit(byte version, int length)
    {
        var data = StoryFixtureBuilder.Create(version, length, includeLengthAndChecksum: false);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("exceeds the", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AcceptsMaximumPhysicalLengthWithoutLengthField(byte version)
    {
        var data = StoryFixtureBuilder.Create(version, 128 * 1024);

        var story = StoryFile.Parse(data);

        Assert.Equal(128 * 1024, story.Length);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    public void AppliesVersionSpecificFileLengthScale(byte version, int scale)
    {
        const int length = 2_048;
        var data = StoryFixtureBuilder.Create(version, length);

        var header = StoryFile.Parse(data).Header;

        Assert.Equal(length, header.FileLength);
        if (version >= 3)
        {
            Assert.Equal((ushort)(length / scale), header.FileLengthWord!.Value);
        }
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    public void AcceptsMaximumRepresentableDeclaredLength(byte version, int scale)
    {
        var length = ushort.MaxValue * scale;
        var data = StoryFixtureBuilder.Create(version, length);

        var story = StoryFile.Parse(data);

        Assert.Equal(length, story.Length);
        Assert.Equal(ushort.MaxValue, story.Header.FileLengthWord!.Value);
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(1025, 1024)]
    public void RejectsInvalidStaticMemoryBase(ushort staticBase, int length)
    {
        var data = StoryFixtureBuilder.Create(5, staticBase: staticBase, length: length);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("Static memory base", exception.Message);
    }

    [Fact]
    public void RejectsHighMemoryThatOverlapsDynamicMemory()
    {
        var data = StoryFixtureBuilder.Create(5, highMemoryBase: 699);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("overlaps dynamic memory", exception.Message);
    }

    [Fact]
    public void AllowsAnEmptyHighMemoryRegionAtEndOfStory()
    {
        var data = StoryFixtureBuilder.Create(5, highMemoryBase: 1024, staticBase: 1024);

        var story = StoryFile.Parse(data);

        Assert.Equal(1024, story.Header.HighMemoryBase);
        Assert.Equal(1024, story.Header.StaticMemoryBase);
    }

    [Fact]
    public void RejectsDictionaryHeaderOutsideStoryFile()
    {
        var data = StoryFixtureBuilder.Create(5, dictionaryAddress: 1022);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("Dictionary header range", exception.Message);
        Assert.Contains("outside the 1024-byte story file", exception.Message);
    }

    [Fact]
    public void AllowsDictionaryInStaticMemory()
    {
        var data = StoryFixtureBuilder.Create(5, dictionaryAddress: 750);

        var story = StoryFile.Parse(data);

        Assert.Equal(750, story.Header.DictionaryAddress);
    }

    [Fact]
    public void RejectsGlobalTableThatCrossesStaticMemoryBoundary()
    {
        var data = StoryFixtureBuilder.Create(5, globalsTableAddress: 300, staticBase: 700);

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("Global variables table range", exception.Message);
    }

    [Fact]
    public void ResolvesVersionSixMainRoutineWithPackedAddressAndOffset()
    {
        var data = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(data, 0x06, 180);
        StoryFixtureBuilder.WriteWord(data, 0x28, 5);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));

        var story = StoryFile.Parse(data);

        Assert.True(story.Header.UsesPackedMainRoutine);
        Assert.Equal(180, story.Header.InitialProgramCounter);
        Assert.Equal((ushort)5, story.Header.RoutinesOffset!.Value);
    }

    [Fact]
    public void AllowsPackedMainRoutineZeroWhenV6RoutineOffsetResolvesIt()
    {
        var data = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(data, 0x06, 0);
        StoryFixtureBuilder.WriteWord(data, 0x28, 50);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));

        var story = StoryFile.Parse(data);

        Assert.Equal(0, story.Header.InitialProgramCounter);
        Assert.Equal((ushort)50, story.Header.RoutinesOffset!.Value);
    }

    [Fact]
    public void RejectsVersionSixMainRoutineOutsideStory()
    {
        var data = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(data, 0x06, 500);
        StoryFixtureBuilder.WriteWord(data, 0x1C, StoryFixtureBuilder.CalculateChecksum(data));

        var exception = Assert.Throws<StoryFileFormatException>(() => StoryFile.Parse(data));

        Assert.Contains("V6 packed main-routine address", exception.Message);
    }

    [Fact]
    public void LoadsStoryFromFileAndExcludesTrailingPadding()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leafpile-story-{Guid.NewGuid():N}.z5");
        var data = StoryFixtureBuilder.Create(5);
        Array.Resize(ref data, data.Length + 2);
        File.WriteAllBytes(path, data);

        try
        {
            var story = StoryFile.Load(path);

            Assert.Equal(StoryFixtureBuilder.DefaultLength, story.Length);
            Assert.Equal(StoryChecksumStatus.Valid, story.Header.ChecksumStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadsStoryFromPathContainingSpaces()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Leafpile story fixtures {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "story with spaces.z3");
        File.WriteAllBytes(path, StoryFixtureBuilder.Create(3));

        try
        {
            Assert.Equal(3, StoryFile.Load(path).Header.Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingStoryFileReportsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-story-{Guid.NewGuid():N}.z3");

        Assert.Throws<FileNotFoundException>(() => StoryFile.Load(path));
    }

    [Fact]
    public void InvalidStoryFileReportsFormatError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"invalid-story-{Guid.NewGuid():N}.z3");
        File.WriteAllBytes(path, new byte[32]);

        try
        {
            Assert.Throws<StoryFileFormatException>(() => StoryFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, sizeof(ushort)));
}
