namespace LeafpileGPT.Tests;

using LeafpileGPT.Core;
using Xunit;

public sealed class ZMachineAuxiliaryFileStoreTests
{
    [Theory]
    [InlineData("prefs", "PREFS.AUX")]
    [InlineData("foo/bar:baz.txt", "FOOBARBA.TXT")]
    [InlineData("ABCDEFGHIJKL.ABCDE", "ABCDEFGH.ABC")]
    [InlineData("/:<>?*|\\", "NULL.AUX")]
    [InlineData("my file.data", "MYFILE.DAT")]
    [InlineData("empty.", "EMPTY.AUX")]
    public void SanitizesNamesToSafeUppercase83Names(string input, string expected)
    {
        Assert.Equal(expected, ZMachineAuxiliaryFileName.Sanitize(input));
        Assert.Equal(expected, ZMachineAuxiliaryFileName.Sanitize(expected));
    }

    [Fact]
    public void ReadsLengthPrefixedFilenameFromStoryMemory()
    {
        var state = CreateState();
        state.Memory.WriteByte(400, 14);
        WriteAscii(state.Memory, 401, "prefs/path.txt");

        Assert.Equal("PREFSPAT.TXT", ZMachineAuxiliaryFileName.FromStoryMemory(state.Memory, 400));
    }

    [Fact]
    public void RejectsTruncatedFilenameArrays()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        bytes[900] = 255;
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        var state = ZMachineExecutionState.Start(StoryFile.Parse(bytes));

        Assert.Throws<ZMachineMemoryAccessException>(() => ZMachineAuxiliaryFileName.FromStoryMemory(state.Memory, 900));
    }

    [Fact]
    public async Task ReadsAndWritesOnlyWithinConfiguredDirectory()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileSystemZMachineAuxiliaryFileStore(directory);
            var contents = new byte[] { 0, 1, 2, 255 };

            await store.WriteAsync("folder/unsafe.sav", contents, TestContext.Current.CancellationToken);

            Assert.Equal(contents, await store.TryReadAsync("folder/unsafe.sav", TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(directory, "FOLDERUN.SAV")));
            Assert.False(File.Exists(Path.Combine(Directory.GetParent(directory)!.FullName, "FOLDER")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReturnsNullOnlyForMissingFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"leafpile-aux-{Guid.NewGuid():N}");
        var store = new FileSystemZMachineAuxiliaryFileStore(directory);

        Assert.Null(await store.TryReadAsync("MISSING.AUX", TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task WrapsStorageFailuresWithOperationAndSafeFilename()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"leafpile-aux-{Guid.NewGuid():N}");
        File.WriteAllText(directory, "not a directory");
        try
        {
            var store = new FileSystemZMachineAuxiliaryFileStore(directory);

            var exception = await Assert.ThrowsAsync<ZMachineAuxiliaryFileException>(async () =>
                await store.WriteAsync("PREFS", new byte[] { 1 }, TestContext.Current.CancellationToken));
            var readException = await Assert.ThrowsAsync<ZMachineAuxiliaryFileException>(async () =>
                await store.TryReadAsync("PREFS", TestContext.Current.CancellationToken));

            Assert.Equal("write", exception.Operation);
            Assert.Equal("PREFS.AUX", exception.FileName);
            Assert.IsAssignableFrom<IOException>(exception.InnerException);
            Assert.Equal("read", readException.Operation);
            Assert.Equal("PREFS.AUX", readException.FileName);
            Assert.IsAssignableFrom<IOException>(readException.InnerException);
        }
        finally
        {
            File.Delete(directory);
        }
    }

    [Fact]
    public void RequiresAConfiguredStorageDirectory()
    {
        Assert.Throws<ArgumentException>(() => new FileSystemZMachineAuxiliaryFileStore(" "));
    }

    private static ZMachineExecutionState CreateState()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        return ZMachineExecutionState.Start(StoryFile.Parse(bytes));
    }

    private static void WriteAscii(ZMachineMemory memory, ushort address, string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            memory.WriteByte(checked((ushort)(address + index)), checked((byte)value[index]));
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"leafpile-aux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
