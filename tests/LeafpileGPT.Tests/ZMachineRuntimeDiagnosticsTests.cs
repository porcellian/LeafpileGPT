using System.Collections.Immutable;
using System.Text.Json;
using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class ZMachineRuntimeDiagnosticsTests
{
    [Fact]
    public void DefaultPolicyReportsOnlyTheFirstRecoverableErrorOfEachType()
    {
        var log = new MemoryRuntimeLog();
        var manager = new ZMachineRuntimeErrorManager(log);

        var first = manager.Report(new ZMachineExecutionException("first"), Identity, 0x1234, null, [], [], timestamp: Timestamp);
        var repeated = manager.Report(new ZMachineExecutionException("second"), Identity, 0x1235, null, [], [], timestamp: Timestamp);
        var otherType = manager.Report(new ZMachineMemoryAccessException(0x200, "read", "out of range"), Identity, 0x1236, null, [], [], timestamp: Timestamp);

        Assert.Equal(ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType, manager.Policy);
        Assert.True(first.ShouldNotify);
        Assert.False(repeated.ShouldNotify);
        Assert.True(otherType.ShouldNotify);
        Assert.All([first, repeated, otherType], diagnostic => Assert.Equal(ZMachineRuntimeErrorSeverity.Warning, diagnostic.Severity));
        Assert.Equal(3, log.Records.Count);
        Assert.Equal(3, manager.GetHistory().Length);
    }

    [Fact]
    public void SuppressAndReportEveryPoliciesOnlyChangeUserNotification()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog(), ZMachineRuntimeErrorPolicy.SuppressReports);
        var suppressed = manager.Report(new ZMachineExecutionException("suppressed"), Identity, 1, null, [], []);

        manager.SetPolicy(ZMachineRuntimeErrorPolicy.ReportEveryOccurrence);
        var first = manager.Report(new ZMachineExecutionException("first"), Identity, 2, null, [], []);
        var second = manager.Report(new ZMachineExecutionException("second"), Identity, 3, null, [], []);

        Assert.False(suppressed.ShouldNotify);
        Assert.True(first.ShouldNotify);
        Assert.True(second.ShouldNotify);
        Assert.False(suppressed.ShouldStop);
        Assert.False(first.ShouldStop);
    }

    [Fact]
    public void StopOnAnyErrorEscalatesRecoverableRuntimeErrorsToBlocking()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog(), ZMachineRuntimeErrorPolicy.StopOnAnyError);

        var diagnostic = manager.Report(new ZMachineExecutionException("invalid operation"), Identity, 0x352, null, [], []);

        Assert.True(diagnostic.ShouldNotify);
        Assert.True(diagnostic.ShouldStop);
        Assert.Equal(ZMachineRuntimeErrorSeverity.Blocking, diagnostic.Severity);
    }

    [Fact]
    public void HostWarningsAreLoggedAndReportedWithoutStoppingEvenUnderStopOnAnyError()
    {
        var log = new MemoryRuntimeLog();
        var manager = new ZMachineRuntimeErrorManager(log, ZMachineRuntimeErrorPolicy.StopOnAnyError);

        var diagnostic = manager.ReportHostWarning(
            new ZMachineExecutionException("audio output unavailable"),
            Identity,
            0x1234,
            Timestamp);

        Assert.Equal(ZMachineRuntimeErrorSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.ShouldNotify);
        Assert.False(diagnostic.ShouldStop);
        Assert.Single(log.Records);
        Assert.Single(manager.GetHistory());
    }

    [Fact]
    public void FatalErrorsAreAlwaysReportedAndStopEvenWhenReportsAreSuppressed()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog(), ZMachineRuntimeErrorPolicy.SuppressReports);

        var diagnostic = manager.Report(new InvalidDataException("bad instruction"), Identity, 0x400, null, [], [], fatal: true);

        Assert.True(diagnostic.ShouldNotify);
        Assert.True(diagnostic.ShouldStop);
        Assert.Equal(ZMachineRuntimeErrorSeverity.Blocking, diagnostic.Severity);
    }

    [Fact]
    public void DiagnosticCopyIncludesGameInstructionExceptionAndMachineStackDetails()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog());
        var instruction = new DecodedInstruction(
            0x1234,
            0x1238,
            InstructionForm.Long,
            OpcodeClass.TwoOperand,
            23,
            new OpcodeDefinition(OpcodeClass.TwoOperand, 23, "div", 1, 8, 2, 2, StoresResult: true),
            ImmutableArray<DecodedOperand>.Empty,
            16,
            null,
            ImmutableArray<byte>.Empty);
        var frame = new ExecutionFrameSnapshot(0, null, null, null, [3, 4], 0, 0, true, 0x1200);

        var diagnostic = manager.Report(
            new ZMachineExecutionException("division by zero"),
            Identity,
            0x1234,
            instruction,
            [frame],
            [8, 9],
            timestamp: Timestamp);

        Assert.Contains("fixture.z5", diagnostic.CopyableDetails);
        Assert.Contains("0x001234", diagnostic.CopyableDetails);
        Assert.Contains("Long opcode 23 (div)", diagnostic.CopyableDetails);
        Assert.Contains("division by zero", diagnostic.CopyableDetails);
        Assert.Contains("frame 0 (root)", diagnostic.CopyableDetails);
        Assert.Contains("evaluation=[8,9]", diagnostic.CopyableDetails);
    }

    [Fact]
    public void HistoryIsBoundedToTheMostRecentThousandErrors()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog(), ZMachineRuntimeErrorPolicy.SuppressReports);
        for (var index = 0; index < 1_005; index++)
        {
            manager.Report(new ZMachineExecutionException(index.ToString()), Identity, (uint)index, null, [], []);
        }

        var history = manager.GetHistory();
        Assert.Equal(1_000, history.Length);
        Assert.Equal("5", history[0].Message);
        Assert.Equal("1004", history[^1].Message);
    }

    [Fact]
    public void JsonLineLoggerWritesStructuredContextWithoutCommandOrStoryOutputFields()
    {
        using var directory = new TemporaryDirectory();
        var logger = new JsonLineZMachineRuntimeLog(directory.Path, clock: () => Timestamp);
        var manager = new ZMachineRuntimeErrorManager(logger);
        manager.Report(new ZMachineExecutionException("controlled runtime failure"), Identity, 0x352, null, [], [], timestamp: Timestamp);

        var file = Assert.Single(Directory.GetFiles(directory.Path, "*.jsonl"));
        var line = File.ReadAllText(file);
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal("LeafpileGPT.Core.ZMachineExecutionException", root.GetProperty("ErrorType").GetString());
        Assert.Equal(Identity.StoryPath, root.GetProperty("StoryPath").GetString());
        Assert.Equal(0x352u, root.GetProperty("ProgramCounter").GetUInt32());
        Assert.False(root.TryGetProperty("Commands", out _));
        Assert.False(root.TryGetProperty("Output", out _));
        Assert.DoesNotContain("look around", line);
        Assert.DoesNotContain("You are in a room", line);
        Assert.Null(logger.LastWriteFailure);
    }

    [Fact]
    public void LoggerRotatesFilesAndPrunesExpiredLogs()
    {
        using var directory = new TemporaryDirectory();
        var logger = new JsonLineZMachineRuntimeLog(directory.Path, maximumFileBytes: 700, clock: () => Timestamp);
        var manager = new ZMachineRuntimeErrorManager(logger, ZMachineRuntimeErrorPolicy.ReportEveryOccurrence);
        for (var index = 0; index < 4; index++)
        {
            manager.Report(new ZMachineExecutionException($"failure {index}"), Identity, (uint)index, null, [], [], timestamp: Timestamp);
        }

        var files = Directory.GetFiles(directory.Path, "*.jsonl");
        Assert.True(files.Length > 1);
        Assert.All(files, file => Assert.True(new FileInfo(file).Length <= 700));

        File.SetLastWriteTimeUtc(files[0], Timestamp.AddDays(-31).UtcDateTime);
        var laterLogger = new JsonLineZMachineRuntimeLog(directory.Path, clock: () => Timestamp);
        Assert.True(laterLogger.TryWrite(CreateRecord("fresh")));
        Assert.DoesNotContain(files[0], Directory.GetFiles(directory.Path, "*.jsonl"));
    }

    [Fact]
    public void LoggerCapsTotalStorageByDeletingOldestFiles()
    {
        using var directory = new TemporaryDirectory();
        var logger = new JsonLineZMachineRuntimeLog(
            directory.Path,
            maximumFileBytes: 1_200,
            maximumStorageBytes: 1_200,
            clock: () => Timestamp);
        for (var index = 0; index < 8; index++)
        {
            Assert.True(logger.TryWrite(CreateRecord($"storage test {index}")));
        }

        Assert.True(Directory.GetFiles(directory.Path, "*.jsonl").Sum(path => new FileInfo(path).Length) <= 1_200);
    }

    [Fact]
    public void LoggerFailureDoesNotEscapeIntoRuntimeErrorHandling()
    {
        using var directory = new TemporaryDirectory();
        var fileInsteadOfDirectory = Path.Combine(directory.Path, "logs");
        File.WriteAllText(fileInsteadOfDirectory, "not a directory");
        var logger = new JsonLineZMachineRuntimeLog(fileInsteadOfDirectory);
        var manager = new ZMachineRuntimeErrorManager(logger);
        var eventRaised = false;
        manager.DiagnosticAdded += _ => eventRaised = true;

        var exception = Record.Exception(() => manager.Report(
            new ZMachineExecutionException("runtime failure"), Identity, 0x352, null, [], []));

        Assert.Null(exception);
        Assert.NotNull(logger.LastWriteFailure);
        Assert.True(eventRaised);
        Assert.Single(manager.GetHistory());
        File.Delete(fileInsteadOfDirectory);
    }

    [Fact]
    public void RuntimeManagerSurvivesAThrowingLogImplementation()
    {
        var manager = new ZMachineRuntimeErrorManager(new ThrowingRuntimeLog());

        var exception = Record.Exception(() => manager.Report(
            new ZMachineExecutionException("runtime failure"), Identity, 0x352, null, [], []));

        Assert.Null(exception);
        Assert.Single(manager.GetHistory());
    }

    [Fact]
    public void ThrowingDiagnosticObserverCannotInterruptRuntimeReporting()
    {
        var manager = new ZMachineRuntimeErrorManager(new MemoryRuntimeLog());
        manager.DiagnosticAdded += _ => throw new InvalidOperationException("UI observer failed");

        var exception = Record.Exception(() => manager.Report(
            new ZMachineExecutionException("runtime failure"), Identity, 0x352, null, [], []));

        Assert.Null(exception);
        Assert.Single(manager.GetHistory());
    }

    private static readonly DateTimeOffset Timestamp = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly ZMachineGameIdentity Identity = new("/stories/fixture.z5", 5, 42, "260101");

    private static ZMachineRuntimeErrorRecord CreateRecord(string message) => new(
        Timestamp,
        typeof(ZMachineExecutionException).FullName!,
        message,
        ZMachineRuntimeErrorSeverity.Warning,
        Identity.StoryPath,
        Identity.Version,
        Identity.ReleaseNumber,
        Identity.SerialNumber,
        0x352,
        "Long",
        23,
        "div",
        message,
        "evaluation=[]",
        true,
        false);

    private sealed class MemoryRuntimeLog : IZMachineRuntimeLog
    {
        public List<ZMachineRuntimeErrorRecord> Records { get; } = [];

        public bool TryWrite(ZMachineRuntimeErrorRecord record)
        {
            Records.Add(record);
            return true;
        }
    }

    private sealed class ThrowingRuntimeLog : IZMachineRuntimeLog
    {
        public bool TryWrite(ZMachineRuntimeErrorRecord record) => throw new IOException("disk failed");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = Directory.CreateTempSubdirectory("leafpile-runtime-log-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
