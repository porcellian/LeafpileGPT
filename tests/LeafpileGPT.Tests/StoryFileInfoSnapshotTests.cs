using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using LeafpileGPT.Core;
using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class StoryFileInfoSnapshotTests
{
    [Fact]
    public void StandaloneSnapshotIncludesFileIdentityChecksumAndAbsentOptionalMetadata()
    {
        using var directory = new TemporaryDirectory();
        var bytes = StoryFixtureBuilder.Create(5);
        var paddedSource = bytes.Concat(new byte[] { 0xAA, 0xBB, 0xCC }).ToArray();
        var path = Path.Combine(directory.Path, "fixture story.z5");
        File.WriteAllBytes(path, paddedSource);

        var story = StoryFile.Load(path);
        var snapshot = CreateSnapshot(story, sourcePath: path);

        Assert.Equal(path, snapshot.SourcePath);
        Assert.Equal("fixture story.z5", snapshot.SourceFileName);
        Assert.Equal(File.GetLastWriteTimeUtc(path), snapshot.SourceFileLastModifiedUtc);
        Assert.Equal("Standalone Z-machine story", snapshot.SourceFormat);
        Assert.Equal(paddedSource.Length, snapshot.SourceFileSizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(paddedSource)), snapshot.SourceFileSha256);
        Assert.Equal(bytes.Length, snapshot.LoadedStorySizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.LoadedStorySha256);
        Assert.Equal((byte)5, snapshot.Version);
        Assert.Equal((ushort)42, snapshot.ReleaseNumber);
        Assert.Equal("840102", snapshot.SerialNumber);
        Assert.Equal("Not present", snapshot.Ifid);
        Assert.Equal("Not present", snapshot.Title);
        Assert.Equal("Not present", snapshot.Author);
        Assert.Equal("Valid (stored and calculated values match)", snapshot.ChecksumStatus);
        Assert.Equal(snapshot.StoredChecksum, snapshot.CalculatedChecksum);
        Assert.Equal(1024, snapshot.EffectiveStorySizeBytes);
        Assert.Equal((ushort)256, snapshot.DeclaredLengthWord);
        Assert.Equal(1024, snapshot.DeclaredStorySizeBytes);
        Assert.False(snapshot.IsBlorb);
        Assert.Equal("Not present", snapshot.BlorbGameIdentifier);
        Assert.Equal("Not present", snapshot.IfmdStatus);
        Assert.Equal("Not present", snapshot.BlorbReleaseNumber);
        Assert.Equal("Not present", snapshot.Frontispiece);
        Assert.Equal("Not present", snapshot.SelectedExecutable);
        Assert.Empty(snapshot.ResourceCounts);
        Assert.Equal(4, snapshot.OriginalRawHeaderLines.Length);
    }

    [Fact]
    public void SnapshotReportsMissingSourceFileDateAsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = Path.Combine(directory.Path, "missing-story.z5");
        var snapshot = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(5)), sourcePath: missingPath);

        Assert.Null(snapshot.SourceFileLastModifiedUtc);
    }

    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(3, false, false, false)]
    [InlineData(4, true, false, false)]
    [InlineData(5, true, true, false)]
    [InlineData(6, true, true, true)]
    [InlineData(7, true, true, true)]
    [InlineData(8, true, true, false)]
    public void HeaderFieldListIncludesOnlyFieldsDefinedForEachVersion(
        byte version,
        bool hasInterpreter,
        bool hasScreenUnits,
        bool hasPackedOffsets)
    {
        var story = StoryFile.Parse(StoryFixtureBuilder.Create(version));
        var snapshot = CreateSnapshot(story);
        var names = snapshot.HeaderFields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(version, snapshot.Version);
        Assert.Contains("Flags 1", names);
        Assert.Contains("Flags 2", names);
        Assert.Contains("Standard revision", names);
        Assert.Equal(hasInterpreter, names.Contains("Interpreter number"));
        Assert.Equal(hasScreenUnits, names.Contains("Screen width units"));
        Assert.Equal(hasPackedOffsets, names.Contains("Routines offset"));
        Assert.Equal(version >= 2, names.Contains("Abbreviations table"));
        Assert.Equal(version >= 3, names.Contains("Checksum"));
        Assert.Equal(version >= 5, names.Contains("Header extension table"));
    }

    [Fact]
    public void V6SnapshotResolvesPackedInitialMainRoutineWithRoutinesOffset()
    {
        var bytes = StoryFixtureBuilder.Create(6);
        StoryFixtureBuilder.WriteWord(bytes, 0x28, 3);
        var story = StoryFile.Parse(bytes);

        var snapshot = CreateSnapshot(story);

        Assert.True(snapshot.UsesPackedV6MainRoutine);
        Assert.Equal((ushort)200, snapshot.InitialProgramCounterValue);
        Assert.Equal((uint)824, snapshot.InitialProgramCounterByteAddress);
    }

    [Fact]
    public void RuntimeHeaderFieldsAreShownBesideOriginalOnDiskValues()
    {
        var original = StoryFixtureBuilder.Create(5);
        original[0x21] = 80;
        StoryFixtureBuilder.WriteWord(original, 0x1C, StoryFixtureBuilder.CalculateChecksum(original));
        var story = StoryFile.Parse(original);
        var runtimeHeader = story.Header.RawHeader.ToArray();
        runtimeHeader[0x01] = 0x01;
        StoryFixtureBuilder.WriteWord(runtimeHeader, 0x10, 0x0011);
        runtimeHeader[0x21] = 100;
        runtimeHeader[0x2C] = 3;

        var snapshot = CreateSnapshot(story, runtimeHeader: runtimeHeader);

        var flags1 = Assert.Single(snapshot.HeaderFields, field => field.Name == "Flags 1");
        var flags2 = Assert.Single(snapshot.HeaderFields, field => field.Name == "Flags 2");
        var width = Assert.Single(snapshot.HeaderFields, field => field.Name == "Screen width");
        var background = Assert.Single(snapshot.HeaderFields, field => field.Name == "Default background colour");
        Assert.Contains("0x00", flags1.OriginalValue);
        Assert.Contains("colours available: Yes", flags1.RuntimeValue);
        Assert.Contains("0x0000", flags2.OriginalValue);
        Assert.Contains("undo requested: Yes", flags2.RuntimeValue);
        Assert.Equal("80", width.OriginalValue);
        Assert.Equal("100", width.RuntimeValue);
        Assert.Equal("0 (current, 0x00)", background.OriginalValue);
        Assert.Equal("3 (red, 0x03)", background.RuntimeValue);
        Assert.NotEqual(string.Join('|', snapshot.OriginalRawHeaderLines), string.Join('|', snapshot.RuntimeRawHeaderLines));
        Assert.Equal((byte)0, story.Bytes.Span[0x01]);
    }

    [Fact]
    public void ChecksumStatesCoverDefinedAndLegacyHeaderCases()
    {
        var valid = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(3)));
        var legacy = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(3, includeLengthAndChecksum: false)));
        var undefined = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(2)));

        Assert.Equal("Valid (stored and calculated values match)", valid.ChecksumStatus);
        Assert.NotNull(valid.StoredChecksum);
        Assert.NotNull(valid.CalculatedChecksum);
        Assert.Equal("Unavailable (legacy Version 3 header)", legacy.ChecksumStatus);
        Assert.Null(legacy.StoredChecksum);
        Assert.Null(legacy.CalculatedChecksum);
        Assert.Equal("Not defined for this version", undefined.ChecksumStatus);
        Assert.Null(undefined.StoredChecksum);
    }

    [Fact]
    public void BlorbSnapshotIncludesMetadataExecutableAndResourceCounts()
    {
        var storyBytes = StoryFixtureBuilder.Create(5);
        var metadata = Encoding.UTF8.GetBytes("<ifindex><story><ifid>LEAFPILE-42</ifid><title>The Sample Story</title><author>Example Author</author></story></ifindex>");
        var frontispiece = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(frontispiece, 7);
        var containerBytes = ZMachineBlorbTests.BlorbFixtureBuilder.Create(
            [
                new ZMachineBlorbTests.FixtureChunk("ZCOD", storyBytes, "Exec", 0),
                new ZMachineBlorbTests.FixtureChunk("JPEG", [0xFF, 0xD8, 0xFF, 0xC0, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0, 0xFF, 0xD9], "Pict", 7)
            ],
            [
                new ZMachineBlorbTests.FixtureChunk("IFhd", [0x12, 0x34]),
                new ZMachineBlorbTests.FixtureChunk("IFmd", metadata),
                new ZMachineBlorbTests.FixtureChunk("RelN", [0, 42]),
                new ZMachineBlorbTests.FixtureChunk("Fspc", frontispiece)
            ]);
        var blorb = BlorbFile.Parse(containerBytes);
        var story = blorb.ZCodeStory!;

        var snapshot = CreateSnapshot(story, sourcePath: "/fixture/story.blb", blorb: blorb);

        Assert.True(snapshot.IsBlorb);
        Assert.Equal("Blorb container (IFF FORM IFRS)", snapshot.SourceFormat);
        Assert.Equal(containerBytes.Length, snapshot.SourceFileSizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(containerBytes)), snapshot.SourceFileSha256);
        Assert.Equal("LEAFPILE-42", snapshot.Ifid);
        Assert.Equal("The Sample Story", snapshot.Title);
        Assert.Equal("Example Author", snapshot.Author);
        Assert.Equal("1234", snapshot.BlorbGameIdentifier);
        Assert.Contains("bytes", snapshot.IfmdStatus);
        Assert.Equal("42", snapshot.BlorbReleaseNumber);
        Assert.Equal("7", snapshot.Frontispiece);
        Assert.Equal($"ZCOD resource 0 at 0x{blorb.SelectedExecutable!.Start:X6}", snapshot.SelectedExecutable);
        Assert.Equal(1, Assert.Single(snapshot.ResourceCounts, item => item.ChunkType == "ZCOD").Count);
        Assert.Equal(1, Assert.Single(snapshot.ResourceCounts, item => item.ChunkType == "JPEG").Count);
    }

    [Fact]
    public void StoryInfoWindowStartsUnavailableAndRefreshesWhenSnapshotChanges()
    {
        var model = new StoryFileInfoWindowModel();
        Assert.False(model.HasStory);

        var first = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(3)), sourcePath: "first.z3");
        var second = CreateSnapshot(StoryFile.Parse(StoryFixtureBuilder.Create(5)), sourcePath: "second.z5");
        model.SetSnapshot(first);
        Assert.True(model.HasStory);
        Assert.Same(first, model.Snapshot);

        model.SetSnapshot(second);
        Assert.Same(second, model.Snapshot);
        Assert.Equal("second.z5", model.Snapshot!.SourceFileName);

        model.SetSnapshot(null);
        Assert.False(model.HasStory);
    }

    private static StoryFileInfoSnapshot CreateSnapshot(
        StoryFile story,
        string sourcePath = "fixture.z5",
        byte[]? runtimeHeader = null,
        BlorbFile? blorb = null) =>
        StoryFileInfoSnapshot.Create(
            sourcePath,
            story,
            (runtimeHeader ?? story.Header.RawHeader.ToArray()).ToImmutableArray(),
            blorb);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("leafpile-story-info-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
