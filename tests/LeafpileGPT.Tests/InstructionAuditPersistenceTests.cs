using System.Text;
using LeafpileGPT.Core;
using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class InstructionAuditPersistenceTests
{
    [Fact]
    public async Task ExportedJsonLinesRoundTripAllCommandAndInstructionDetails()
    {
        using var directory = new TemporaryDirectory();
        var audit = new ZMachineInstructionAudit();
        audit.BeginCommand("look carefully");
        audit.Record(CreateQuit(0x352), CreateState(0x353));
        var exported = await InstructionAuditExporter.ExportAsync(
            Path.Combine(directory.Path, "roundtrip.pdf"),
            "Fixture Story",
            audit,
            TestContext.Current.CancellationToken);

        var loaded = await InstructionAuditJsonLinesImporter.LoadAsync(exported.JsonLinesPath, TestContext.Current.CancellationToken);

        Assert.False(loaded.IgnoredIncompleteTail);
        Assert.Equal(2, loaded.Groups.Length);
        Assert.Equal("Game started", loaded.Groups[0].Command);
        Assert.Equal("look carefully", loaded.Groups[1].Command);
        var entry = Assert.Single(loaded.Groups[1].Instructions);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(0x352u, entry.ProgramCounter);
        Assert.Equal(0x353u, entry.ProgramCounterAfter);
        Assert.Equal(InstructionForm.Short, entry.Form);
        Assert.Equal(OpcodeClass.ZeroOperand, entry.OpcodeClass);
        Assert.Equal((byte)10, entry.Opcode);
        Assert.Equal("quit", entry.Mnemonic);
        Assert.Empty(entry.Operands);
    }

    [Fact]
    public async Task ImporterIgnoresOnlyAnUnterminatedMalformedFinalRecord()
    {
        using var directory = new TemporaryDirectory();
        var audit = new ZMachineInstructionAudit();
        audit.Record(CreateQuit(0x352), CreateState(0x353));
        var exported = await InstructionAuditExporter.ExportAsync(
            Path.Combine(directory.Path, "partial.pdf"),
            "Fixture Story",
            audit,
            TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(exported.JsonLinesPath, "{\"type\":\"instruction\"", TestContext.Current.CancellationToken);

        var loaded = await InstructionAuditJsonLinesImporter.LoadAsync(exported.JsonLinesPath, TestContext.Current.CancellationToken);

        Assert.True(loaded.IgnoredIncompleteTail);
        Assert.Equal(1, loaded.InstructionCount);
        await File.AppendAllTextAsync(exported.JsonLinesPath, "\n", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => InstructionAuditJsonLinesImporter.LoadAsync(
            exported.JsonLinesPath,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AutomaticAuditContinuesAcrossClearAndWritesPdfWhenDisposed()
    {
        using var directory = new TemporaryDirectory();
        var audit = new ZMachineInstructionAudit();
        var saver = new InstructionAuditAutoSaver(directory.Path, "fixture.z3", audit, exception => throw exception);
        saver.Start();
        audit.BeginCommand("look");
        audit.Record(CreateQuit(0x352), CreateState(0x353));
        audit.ClearHistory();
        audit.Record(CreateQuit(0x354), CreateState(0x355));
        audit.BeginCommand("inventory");
        audit.Record(CreateQuit(0x356), CreateState(0x357));

        await saver.DisposeAsync();

        Assert.NotNull(saver.JsonLinesPath);
        Assert.NotNull(saver.PdfPath);
        Assert.True(File.Exists(saver.JsonLinesPath));
        Assert.True(File.Exists(saver.PdfPath));
        var loaded = await InstructionAuditJsonLinesImporter.LoadAsync(saver.JsonLinesPath!, TestContext.Current.CancellationToken);
        Assert.Equal(["Game started", "look", "inventory"], loaded.Groups.Select(group => group.Command));
        Assert.Equal(3, loaded.InstructionCount);
        var pdf = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(saver.PdfPath!, TestContext.Current.CancellationToken));
        Assert.Contains("Instructions captured: 3", pdf);
        Assert.Contains("COMMAND 2: look", pdf);
        Assert.Contains("COMMAND 4: inventory", pdf);
    }

    [Fact]
    public async Task AutomaticAuditsUseDistinctNamesForRepeatedSessions()
    {
        using var directory = new TemporaryDirectory();
        var audit = new ZMachineInstructionAudit();
        var first = new InstructionAuditAutoSaver(directory.Path, "fixture.z3", audit, exception => throw exception);
        var second = new InstructionAuditAutoSaver(directory.Path, "fixture.z3", audit, exception => throw exception);
        first.Start();
        second.Start();

        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.NotEqual(first.JsonLinesPath, second.JsonLinesPath);
        Assert.True(File.Exists(first.PdfPath));
        Assert.True(File.Exists(second.PdfPath));
    }

    [Fact]
    public async Task DesktopSessionAutomaticallyFinalizesAuditOnDisposal()
    {
        using var directory = new TemporaryDirectory();
        var auditDirectory = Path.Combine(directory.Path, "Audits");
        var saveDirectory = Path.Combine(directory.Path, "Save");
        var transcriptDirectory = Path.Combine(directory.Path, "Transcripts");
        Directory.CreateDirectory(saveDirectory);
        Directory.CreateDirectory(transcriptDirectory);
        var storyBytes = StoryFixtureBuilder.Create(3);
        storyBytes[850] = 0xBA;
        StoryFixtureBuilder.WriteWord(storyBytes, 0x1C, StoryFixtureBuilder.CalculateChecksum(storyBytes));
        var session = new DesktopGameSession(
            StoryFile.Parse(storyBytes),
            "fixture.z3",
            new GameDisplayModel(),
            _ => { },
            saveDirectory,
            transcriptDirectory,
            dispatch: action => action(),
            auditsDirectory: auditDirectory);

        session.Start();
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        var jsonLinesPath = Assert.Single(Directory.GetFiles(auditDirectory, "*.jsonl"));
        var pdfPath = Assert.Single(Directory.GetFiles(auditDirectory, "*.pdf"));
        var loaded = await InstructionAuditJsonLinesImporter.LoadAsync(jsonLinesPath, TestContext.Current.CancellationToken);
        Assert.Equal(1, loaded.InstructionCount);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task AutomaticAuditWriteFailureIsReportedAndDoesNotChangeAuditExecution()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var blocker = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "blocker", TestContext.Current.CancellationToken);
        using var reported = new ManualResetEventSlim();
        Exception? failure = null;
        var audit = new ZMachineInstructionAudit();
        var saver = new InstructionAuditAutoSaver(blocker, "fixture.z3", audit, exception =>
        {
            failure = exception;
            reported.Set();
        });
        saver.Start();
        var recorded = audit.Record(CreateQuit(0x352), CreateState(0x353));

        await saver.DisposeAsync();

        Assert.True(reported.IsSet);
        Assert.NotNull(failure);
        Assert.Equal(1, recorded.Sequence);
        Assert.Equal(1, audit.InstructionsExecuted);
    }

    [Fact]
    public async Task ImportedTraceCanBeExportedAgainWithoutChangingItsData()
    {
        using var directory = new TemporaryDirectory();
        var audit = new ZMachineInstructionAudit();
        audit.BeginCommand("wait");
        audit.Record(CreateQuit(0x352), CreateState(0x353));
        var source = await InstructionAuditExporter.ExportAsync(
            Path.Combine(directory.Path, "source.pdf"),
            "Fixture Story",
            audit,
            TestContext.Current.CancellationToken);
        var imported = await InstructionAuditJsonLinesImporter.LoadAsync(source.JsonLinesPath, TestContext.Current.CancellationToken);

        var copy = await InstructionAuditExporter.ExportAsync(
            Path.Combine(directory.Path, "copy.pdf"),
            "Fixture Story",
            imported.Groups,
            TestContext.Current.CancellationToken);
        var loadedCopy = await InstructionAuditJsonLinesImporter.LoadAsync(copy.JsonLinesPath, TestContext.Current.CancellationToken);

        Assert.Equal(imported.Groups.Select(group => (group.Number, group.Command, group.StartedAt)),
            loadedCopy.Groups.Select(group => (group.Number, group.Command, group.StartedAt)));
        Assert.Equal(imported.InstructionCount, loadedCopy.InstructionCount);
        Assert.Equal(imported.Groups.SelectMany(group => group.Instructions).Select(entry => (entry.Sequence, entry.ProgramCounter, entry.Mnemonic)),
            loadedCopy.Groups.SelectMany(group => group.Instructions).Select(entry => (entry.Sequence, entry.ProgramCounter, entry.Mnemonic)));
    }

    [Fact]
    public void TraceSelectionKeepsLiveCaptureRunningWhileArchiveIsDisplayed()
    {
        var liveAudit = new ZMachineInstructionAudit();
        var selection = new InstructionAuditTraceSelection();
        selection.SetLiveAudit(liveAudit);
        var archived = new ZMachineInstructionAudit();
        archived.BeginCommand("saved command");
        archived.Record(CreateQuit(0x352), CreateState(0x353));
        selection.LoadArchive(archived.GetSnapshot());

        liveAudit.BeginCommand("new live command");
        liveAudit.Record(CreateQuit(0x354), CreateState(0x355));

        Assert.True(selection.IsShowingArchive);
        Assert.False(selection.CanClear);
        Assert.True(selection.CanReturnToLive);
        Assert.Equal("saved command", selection.GetGroupInfo(1)!.Command);
        selection.ReturnToLive();
        Assert.False(selection.IsShowingArchive);
        Assert.True(selection.CanClear);
        Assert.Equal("new live command", selection.GetGroupInfo(1)!.Command);
        selection.ClearLiveHistory();
        Assert.Equal(1, selection.GroupCount);
    }

    [Fact]
    public void TraceSelectionAllowsAnArchiveWithoutAnActiveStory()
    {
        var selection = new InstructionAuditTraceSelection();
        var audit = new ZMachineInstructionAudit();
        selection.LoadArchive(audit.GetSnapshot());

        Assert.True(selection.IsShowingArchive);
        Assert.False(selection.CanReturnToLive);
        Assert.False(selection.CanClear);
        Assert.Equal("Game started", selection.GetGroupInfo(0)!.Command);
    }

    private static ZMachineExecutionState CreateState(uint programCounter)
    {
        var state = ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(3)));
        state.SetProgramCounter(programCounter);
        return state;
    }

    private static DecodedInstruction CreateQuit(uint programCounter) => new(
        programCounter,
        programCounter + 1,
        InstructionForm.Short,
        OpcodeClass.ZeroOperand,
        10,
        new OpcodeDefinition(OpcodeClass.ZeroOperand, 10, "quit", 1, 8, 0, 0),
        [],
        null,
        null,
        []);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leafpile-audit-" + Guid.NewGuid().ToString("N"));

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
