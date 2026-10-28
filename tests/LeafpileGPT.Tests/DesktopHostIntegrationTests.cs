using LeafpileGPT.Core;
using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class DesktopHostIntegrationTests
{
    [Fact]
    public async Task DesktopInputProviderDeliversLinesAndEmptyCommands()
    {
        var provider = CreateInputProvider();
        provider.SubmitLine("look mailbox");
        provider.SubmitLine(string.Empty);

        var request = new ZMachineLineInputRequest(0, string.Empty, 30, [], true);
        var command = await provider.ReadLineAsync(request, TestContext.Current.CancellationToken);
        var empty = await provider.ReadLineAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new ZMachineLineInputResult("look mailbox", 13), command);
        Assert.Equal(new ZMachineLineInputResult(string.Empty, 13), empty);
        Assert.False(provider.SupportsTimedInput);
    }

    [Fact]
    public async Task DesktopInputProviderUsesTheFirstSubmittedCharacterForReadChar()
    {
        var provider = CreateInputProvider();
        provider.SubmitLine("x");

        Assert.Equal((ushort)'x', await provider.ReadCharacterAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DesktopEventServicesForwardSoundRequestsAndReportPlaybackUnavailable()
    {
        var services = new DesktopZMachineEventServices();
        var request = new ZMachineSoundEffectRequest(7, ZMachineSoundAction.Start, 8, 1, 0);
        ZMachineSoundEffectRequest? forwarded = null;
        services.SoundEffectRequested += value => forwarded = value;

        services.PerformSoundEffect(request, completed: null);

        Assert.False(services.SupportsSoundEffects);
        Assert.Equal(request, forwarded);
    }

    [Fact]
    public void DesktopSaveStoreWritesStoryScopedSafeFilesAndReturnsNullWhenMissing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new DesktopZMachineSaveStore(directory, "story with spaces.z3");
            var bytes = new byte[] { 1, 2, 3, 4 };

            Assert.True(store.Save(bytes, new ZMachineSaveOptions(SuggestedName: "../custom slot.sav")));

            var expectedPath = Path.Combine(directory, "custom_slot.ifzs");
            Assert.Equal(expectedPath, store.ResolvePath(new ZMachineSaveOptions(SuggestedName: "../custom slot.sav")));
            Assert.Equal(bytes, File.ReadAllBytes(expectedPath));
            Assert.Equal(bytes, store.Restore(new ZMachineSaveOptions(SuggestedName: "../custom slot.sav")));
            Assert.Null(store.Restore(new ZMachineSaveOptions(SuggestedName: "missing")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TranscriptWriterCreatesAnAppDataFileOnlyWhenTranscriptOutputArrives()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var writer = new DesktopTranscriptWriter(directory, "story.z5", DateTimeOffset.UnixEpoch);
            Assert.Null(writer.FilePath);

            writer.Write("Opening text.\n");
            writer.Write("A command response.\n");

            Assert.NotNull(writer.FilePath);
            Assert.StartsWith(directory, writer.FilePath, StringComparison.Ordinal);
            writer.Dispose();
            Assert.Equal("Opening text.\nA command response.\n", File.ReadAllText(writer.FilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TranscriptWriterReportsDiskFailureWithoutThrowingIntoTheVm()
    {
        var root = CreateTemporaryDirectory();
        var fileUsedAsDirectory = Path.Combine(root, "not-a-directory");
        File.WriteAllText(fileUsedAsDirectory, "occupied");
        using var writer = new DesktopTranscriptWriter(fileUsedAsDirectory, "story.z3");
        Exception? failure = null;
        writer.WriteFailed += exception => failure = exception;

        var exception = Record.Exception(() => writer.Write("captured text"));
        writer.Write("later text");

        Assert.Null(exception);
        Assert.IsAssignableFrom<IOException>(failure);
        File.Delete(fileUsedAsDirectory);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task SessionRunsStoryInstructionsAndConnectsScreenOutput()
    {
        var bytes = StoryFixtureBuilder.Create(3);
        bytes[850] = 0xE4; // sread
        bytes[851] = 0x0F; // two large constants, no further operands
        StoryFixtureBuilder.WriteWord(bytes, 852, 0x0280);
        StoryFixtureBuilder.WriteWord(bytes, 854, 0x02A0);
        bytes[856] = 0xBA; // quit
        bytes[0x0280] = 20;
        bytes[0x02A0] = 0;
        bytes[200] = 0; // no dictionary separators
        bytes[201] = 4; // V3 dictionary entry size
        bytes[202] = 0;
        bytes[203] = 0; // empty dictionary
        StoryFixtureBuilder.WriteWord(bytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(bytes));
        var story = StoryFile.Parse(bytes);
        var display = new GameDisplayModel();
        var saveDirectory = CreateTemporaryDirectory();
        var transcriptDirectory = CreateTemporaryDirectory();
        var session = new DesktopGameSession(
            story,
            "fixture.z3",
            display,
            _ => { },
            saveDirectory,
            transcriptDirectory,
            dispatch: action => action());
        var inputRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.InputRequested += _ => inputRequested.TrySetResult();

        try
        {
            await using (session)
            {
                var screen = session.State.OutputStreams.Screen;
                Assert.False(screen.MorePromptsEnabled);
                screen.SetDimensions(80, 5);
                screen.WriteText(new string('\n', 10), _ => { });
                Assert.False(screen.IsWaitingForMore);
                session.Start();
                await inputRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                var save = await session.CaptureSaveStateAsync(TestContext.Current.CancellationToken);
                Assert.Equal((uint)850, save.Identity.ProgramCounter);
                await session.ApplySaveStateAsync(save, TestContext.Current.CancellationToken);
                session.SubmitCommand("look");
                await session.Completion;

                Assert.True(session.State.IsHalted);
                Assert.Equal("look\n", display.Transcript);
            }
        }
        finally
        {
            Directory.Delete(saveDirectory, recursive: true);
            Directory.Delete(transcriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DesktopSessionKeepsMorePromptsDisabledAfterRestart()
    {
        var saveDirectory = CreateTemporaryDirectory();
        var transcriptDirectory = CreateTemporaryDirectory();
        var session = new DesktopGameSession(
            StoryFile.Parse(StoryFixtureBuilder.Create(3)),
            "fixture.z3",
            new GameDisplayModel(),
            _ => { },
            saveDirectory,
            transcriptDirectory,
            dispatch: action => action());

        try
        {
            await using (session)
            {
                Assert.False(session.State.OutputStreams.Screen.MorePromptsEnabled);

                await session.RestartAsync(TestContext.Current.CancellationToken);

                Assert.False(session.State.OutputStreams.Screen.MorePromptsEnabled);
            }
        }
        finally
        {
            Directory.Delete(saveDirectory, recursive: true);
            Directory.Delete(transcriptDirectory, recursive: true);
        }
    }

    private static DesktopInputProvider CreateInputProvider() =>
        new(new ZMachineMemory(StoryFile.Parse(StoryFixtureBuilder.Create(3))));

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leafpile-host-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

}
