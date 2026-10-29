using LeafpileGPT.Core;
using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class ZMachineInstructionAuditTests
{
    [Fact]
    public void AuditGroupsCommandsAndAssignsMonotonicSequenceAndProgramCounters()
    {
        var state = CreateState();
        var audit = new ZMachineInstructionAudit();

        audit.Record(CreateQuit(0x352), state);
        audit.BeginCommand("look around");
        state.SetProgramCounter(0x356);
        var second = audit.Record(CreateQuit(0x354), state);
        var groups = audit.GetSnapshot();

        Assert.Equal(2, groups.Length);
        Assert.Equal("Game started", groups[0].Command);
        Assert.Equal("look around", groups[1].Command);
        Assert.Equal(1, groups[0].Instructions[0].Sequence);
        Assert.Equal(0x352u, groups[0].Instructions[0].ProgramCounter);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(0x354u, second.ProgramCounter);
        Assert.Equal(0x356u, second.ProgramCounterAfter);
        Assert.Equal("quit", second.Mnemonic);
    }

    [Fact]
    public void ClearHistoryKeepsRecordingAndDoesNotResetGlobalInstructionSequence()
    {
        var state = CreateState();
        var audit = new ZMachineInstructionAudit();
        audit.Record(CreateQuit(0x352), state);

        audit.ClearHistory();
        var entry = audit.Record(CreateQuit(0x354), state);

        Assert.Single(audit.GetSnapshot());
        Assert.Equal(2, entry.Sequence);
        Assert.Equal("Game started", audit.GetSnapshot()[0].Command);
    }

    [Fact]
    public void ThrowingViewerObserverDoesNotAffectAuditRecording()
    {
        var state = CreateState();
        var audit = new ZMachineInstructionAudit();
        var notified = false;
        audit.Changed += () => throw new InvalidOperationException("viewer failed");
        audit.Changed += () => notified = true;

        var exception = Record.Exception(() => audit.Record(CreateQuit(0x352), state));

        Assert.Null(exception);
        Assert.True(notified);
        Assert.Equal(1, audit.InstructionsExecuted);
    }

    [Fact]
    public void NavigationMovesAcrossGroupsAndCurrentResumesFollowingLiveOutput()
    {
        var navigation = new ZMachineInstructionAuditNavigation();
        navigation.GroupsChanged(3);
        Assert.Equal(2, navigation.SelectedIndex);
        Assert.True(navigation.IsFollowing);

        navigation.Previous(3);
        Assert.Equal(1, navigation.SelectedIndex);
        Assert.False(navigation.IsFollowing);
        navigation.GroupsChanged(4);
        Assert.Equal(1, navigation.SelectedIndex);

        navigation.Next(4);
        Assert.Equal(2, navigation.SelectedIndex);
        navigation.Current(4);
        Assert.Equal(3, navigation.SelectedIndex);
        Assert.True(navigation.IsFollowing);
        navigation.GroupsChanged(5);
        Assert.Equal(4, navigation.SelectedIndex);
    }

    [Fact]
    public async Task ExportWritesReadablePdfAndCommandAndInstructionJsonLines()
    {
        using var directory = new TemporaryDirectory();
        var state = CreateState();
        var audit = new ZMachineInstructionAudit();
        audit.BeginCommand("look (carefully)");
        audit.Record(CreateQuit(0x352), state);

        var paths = await InstructionAuditExporter.ExportAsync(
            Path.Combine(directory.Path, "run.pdf"),
            "Fixture Story",
            audit,
            TestContext.Current.CancellationToken);

        var pdfBytes = await File.ReadAllBytesAsync(paths.PdfPath, TestContext.Current.CancellationToken);
        var jsonLines = await File.ReadAllLinesAsync(paths.JsonLinesPath, TestContext.Current.CancellationToken);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(pdfBytes));
        Assert.Contains("Fixture Story", System.Text.Encoding.ASCII.GetString(pdfBytes));
        Assert.Equal(3, jsonLines.Length);
        Assert.Contains("\"type\":\"command\"", jsonLines[0]);
        Assert.Contains("look (carefully)", jsonLines[1]);
        Assert.Contains("\"type\":\"instruction\"", jsonLines[2]);
        Assert.Contains("\"sequence\":1", jsonLines[2]);
        Assert.Contains("\"programCounter\":\"0x000352\"", jsonLines[2]);
        Assert.EndsWith(".pdf", paths.PdfPath, StringComparison.Ordinal);
        Assert.EndsWith(".jsonl", paths.JsonLinesPath, StringComparison.Ordinal);

        audit.ClearHistory();
        Assert.True(File.Exists(paths.PdfPath));
        Assert.True(File.Exists(paths.JsonLinesPath));
    }

    private static ZMachineExecutionState CreateState() => ZMachineExecutionState.Start(StoryFile.Parse(StoryFixtureBuilder.Create(3)));

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
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("leafpile-audit-test-").FullName;

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
