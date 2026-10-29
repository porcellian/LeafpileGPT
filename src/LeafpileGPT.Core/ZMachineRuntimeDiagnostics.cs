namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum ZMachineRuntimeErrorPolicy
{
    SuppressReports,
    ReportFirstPerErrorType,
    ReportEveryOccurrence,
    StopOnAnyError
}

public enum ZMachineRuntimeErrorSeverity
{
    Warning,
    Blocking
}

public sealed record ZMachineGameIdentity(string StoryPath, byte Version, ushort ReleaseNumber, string SerialNumber);

public sealed record ZMachineRuntimeErrorRecord(
    DateTimeOffset Timestamp,
    string ErrorType,
    string Message,
    ZMachineRuntimeErrorSeverity Severity,
    string StoryPath,
    byte StoryVersion,
    ushort ReleaseNumber,
    string SerialNumber,
    uint ProgramCounter,
    string? InstructionForm,
    byte? Opcode,
    string? Mnemonic,
    string ExceptionDetails,
    string MachineStackDetails,
    bool ShouldNotify,
    bool ShouldStop)
{
    public string CopyableDetails => string.Join(
        Environment.NewLine,
        $"Time: {Timestamp:O}",
        $"Story: {StoryPath}",
        $"Version: {StoryVersion}; release {ReleaseNumber}; serial {SerialNumber}",
        $"PC: 0x{ProgramCounter:X6}",
        $"Instruction: {(InstructionForm is null ? "unknown" : $"{InstructionForm} opcode {Opcode} ({Mnemonic ?? "unknown"})")}",
        $"Severity: {Severity}",
        $"Error: {ErrorType}: {Message}",
        "Exception details:",
        ExceptionDetails,
        "Z-machine stack:",
        MachineStackDetails);
}

public interface IZMachineRuntimeLog
{
    bool TryWrite(ZMachineRuntimeErrorRecord record);
}

public sealed class NullZMachineRuntimeLog : IZMachineRuntimeLog
{
    public static NullZMachineRuntimeLog Instance { get; } = new();

    private NullZMachineRuntimeLog()
    {
    }

    public bool TryWrite(ZMachineRuntimeErrorRecord record) => true;
}

/// <summary>Writes structured diagnostics while keeping logging failures outside VM control flow.</summary>
public sealed class JsonLineZMachineRuntimeLog : IZMachineRuntimeLog
{
    private const string FilePattern = "leafpile-*.jsonl";
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly TimeSpan _retention;
    private readonly long _maximumStorageBytes;
    private readonly Func<DateTimeOffset> _clock;
    private string? _activePath;

    public JsonLineZMachineRuntimeLog(
        string directory,
        long maximumFileBytes = 10 * 1024 * 1024,
        TimeSpan? retention = null,
        long maximumStorageBytes = 100 * 1024 * 1024,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maximumFileBytes < 1 || maximumStorageBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes), "Log size limits must be positive.");
        }

        _directory = Path.GetFullPath(directory);
        _maximumFileBytes = maximumFileBytes;
        _retention = retention ?? TimeSpan.FromDays(30);
        if (_retention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        _maximumStorageBytes = maximumStorageBytes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Exception? LastWriteFailure { get; private set; }

    public bool TryWrite(ZMachineRuntimeErrorRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var now = _clock().ToUniversalTime();
                PruneExpired(now);
                var entry = new RuntimeLogEntry(
                    record.Timestamp,
                    record.ErrorType,
                    record.Message,
                    record.Severity,
                    record.StoryPath,
                    record.StoryVersion,
                    record.ReleaseNumber,
                    record.SerialNumber,
                    record.ProgramCounter,
                    record.InstructionForm,
                    record.Opcode,
                    record.Mnemonic,
                    record.ExceptionDetails,
                    record.MachineStackDetails);
                var line = JsonSerializer.Serialize(entry, SerializerOptions) + "\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                if (_activePath is not null && File.Exists(_activePath)
                    && new FileInfo(_activePath).Length + bytes.Length > _maximumFileBytes)
                {
                    _activePath = null;
                }

                _activePath ??= Path.Combine(
                    _directory,
                    $"leafpile-{now:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.jsonl");
                using (var stream = new FileStream(_activePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes);
                    stream.Flush();
                }
                PruneToStorageLimit();
                LastWriteFailure = null;
                return true;
            }
            catch (Exception exception)
            {
                LastWriteFailure = exception;
                return false;
            }
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var cutoff = now - _retention;
        foreach (var file in GetLogFiles())
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
            {
                File.Delete(file);
                if (string.Equals(file, _activePath, PathComparison))
                {
                    _activePath = null;
                }
            }
        }
    }

    private void PruneToStorageLimit()
    {
        var files = GetLogFiles()
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (totalBytes <= _maximumStorageBytes)
            {
                break;
            }

            if (string.Equals(file.FullName, _activePath, PathComparison) && files.Count == 1)
            {
                break;
            }

            File.Delete(file.FullName);
            totalBytes -= file.Length;
            if (string.Equals(file.FullName, _activePath, PathComparison))
            {
                _activePath = null;
            }
        }
    }

    private string[] GetLogFiles() => Directory.GetFiles(_directory, FilePattern);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record RuntimeLogEntry(
        DateTimeOffset Timestamp,
        string ErrorType,
        string Message,
        ZMachineRuntimeErrorSeverity Severity,
        string StoryPath,
        byte StoryVersion,
        ushort ReleaseNumber,
        string SerialNumber,
        uint ProgramCounter,
        string? InstructionForm,
        byte? Opcode,
        string? Mnemonic,
        string ExceptionDetails,
        string MachineStackDetails);
}

/// <summary>Applies the selected reporting policy, keeps bounded in-memory history, and logs every diagnostic.</summary>
public sealed class ZMachineRuntimeErrorManager
{
    private const int MaximumHistoryEntries = 1_000;
    private readonly object _sync = new();
    private readonly HashSet<string> _reportedTypes = new(StringComparer.Ordinal);
    private readonly List<ZMachineRuntimeErrorRecord> _history = [];
    private readonly IZMachineRuntimeLog _log;
    private ZMachineRuntimeErrorPolicy _policy;

    public ZMachineRuntimeErrorManager(
        IZMachineRuntimeLog log,
        ZMachineRuntimeErrorPolicy policy = ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        SetPolicy(policy);
    }

    public event Action<ZMachineRuntimeErrorRecord>? DiagnosticAdded;

    public ZMachineRuntimeErrorPolicy Policy
    {
        get
        {
            lock (_sync)
            {
                return _policy;
            }
        }
    }

    public ImmutableArray<ZMachineRuntimeErrorRecord> GetHistory()
    {
        lock (_sync)
        {
            return _history.ToImmutableArray();
        }
    }

    public void SetPolicy(ZMachineRuntimeErrorPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        lock (_sync)
        {
            _policy = policy;
            _reportedTypes.Clear();
        }
    }

    public ZMachineRuntimeErrorRecord Report(
        Exception exception,
        ZMachineGameIdentity identity,
        uint programCounter,
        DecodedInstruction? instruction,
        ImmutableArray<ExecutionFrameSnapshot> callStack,
        ImmutableArray<ushort> evaluationStack,
        bool fatal = false,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(identity);

        var recoverable = !fatal
            && exception is (ZMachineMemoryAccessException or ZMachineExecutionException { IsFatal: false });
        ZMachineRuntimeErrorPolicy policy;
        bool firstOfType;
        lock (_sync)
        {
            policy = _policy;
            var errorType = exception.GetType().FullName ?? exception.GetType().Name;
            firstOfType = _reportedTypes.Add(errorType);
        }

        var shouldStop = !recoverable || policy == ZMachineRuntimeErrorPolicy.StopOnAnyError;
        var shouldNotify = shouldStop || policy switch
        {
            ZMachineRuntimeErrorPolicy.SuppressReports => false,
            ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType => firstOfType,
            ZMachineRuntimeErrorPolicy.ReportEveryOccurrence => true,
            ZMachineRuntimeErrorPolicy.StopOnAnyError => true,
            _ => false
        };
        var machineStack = FormatMachineStack(callStack, evaluationStack);
        var record = new ZMachineRuntimeErrorRecord(
            timestamp ?? DateTimeOffset.UtcNow,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            shouldStop ? ZMachineRuntimeErrorSeverity.Blocking : ZMachineRuntimeErrorSeverity.Warning,
            identity.StoryPath,
            identity.Version,
            identity.ReleaseNumber,
            identity.SerialNumber,
            programCounter,
            instruction?.Form.ToString(),
            instruction?.Opcode,
            instruction?.Definition.Mnemonic,
            exception.ToString(),
            machineStack,
            shouldNotify,
            shouldStop);

        Publish(record);
        return record;
    }

    /// <summary>Logs and reports a recoverable host warning without applying VM stop-on-error policy.</summary>
    public ZMachineRuntimeErrorRecord ReportHostWarning(
        Exception exception,
        ZMachineGameIdentity identity,
        uint programCounter,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(identity);

        var record = new ZMachineRuntimeErrorRecord(
            timestamp ?? DateTimeOffset.UtcNow,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            ZMachineRuntimeErrorSeverity.Warning,
            identity.StoryPath,
            identity.Version,
            identity.ReleaseNumber,
            identity.SerialNumber,
            programCounter,
            null,
            null,
            null,
            exception.ToString(),
            string.Empty,
            ShouldNotify: true,
            ShouldStop: false);
        Publish(record);
        return record;
    }

    private void Publish(ZMachineRuntimeErrorRecord record)
    {
        lock (_sync)
        {
            _history.Add(record);
            if (_history.Count > MaximumHistoryEntries)
            {
                _history.RemoveAt(0);
            }
        }

        try
        {
            _log.TryWrite(record);
        }
        catch
        {
            // Diagnostics must never turn a host warning into a game failure.
        }

        var handlers = DiagnosticAdded;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<ZMachineRuntimeErrorRecord> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(record);
            }
            catch
            {
                // UI observers are not allowed to turn diagnostics into VM failures.
            }
        }
    }

    private static string FormatMachineStack(
        ImmutableArray<ExecutionFrameSnapshot> callStack,
        ImmutableArray<ushort> evaluationStack)
    {
        var builder = new StringBuilder();
        foreach (var frame in callStack)
        {
            builder.Append("frame ").Append(frame.StackFrameIndex)
                .Append(frame.IsRoot ? " (root)" : string.Empty)
                .Append(" routine=").Append(frame.RoutineAddress is { } routine ? $"0x{routine:X6}" : "root")
                .Append(" return=").Append(frame.ReturnProgramCounter is { } returnPc ? $"0x{returnPc:X6}" : "none")
                .Append(" args=").Append(frame.ArgumentCount)
                .Append(" locals=[").AppendJoin(",", frame.Locals).AppendLine("]");
        }

        builder.Append("evaluation=[").AppendJoin(",", evaluationStack).Append(']');
        return builder.ToString();
    }
}
