using System.Collections.Immutable;
using System.Text;
using System.Threading.Channels;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Persists an instruction audit on a worker task without blocking VM execution.</summary>
public sealed class InstructionAuditAutoSaver : IAsyncDisposable
{
    private const int QueueCapacity = 8192;
    private readonly string _directory;
    private readonly string _storyName;
    private readonly ZMachineInstructionAudit _audit;
    private readonly Action<Exception> _writeFailed;
    private readonly Channel<ZMachineInstructionAuditEvent> _events = Channel.CreateBounded<ZMachineInstructionAuditEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private Task? _writerTask;
    private string? _basePath;
    private int _started;
    private int _failed;
    private int _disposed;

    public InstructionAuditAutoSaver(
        string directory,
        string storyName,
        ZMachineInstructionAudit audit,
        Action<Exception> writeFailed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyName);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(writeFailed);
        _directory = Path.GetFullPath(directory);
        _storyName = Path.GetFileNameWithoutExtension(storyName);
        _audit = audit;
        _writeFailed = writeFailed;
    }

    public string? JsonLinesPath => _basePath is null ? null : _basePath + ".jsonl";

    public string? PdfPath => _basePath is null ? null : _basePath + ".pdf";

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _basePath = CreateUniqueBasePath();
        var initialGroups = _audit.GetSnapshot();
        _audit.EventRecorded += OnAuditEvent;
        foreach (var group in initialGroups)
        {
            if (!TryQueue(new ZMachineInstructionAuditEvent(
                    ZMachineInstructionAuditEventKind.CommandStarted,
                    group.Number,
                    group.Command,
                    group.StartedAt,
                    null)))
            {
                break;
            }

            foreach (var instruction in group.Instructions)
            {
                if (!TryQueue(new ZMachineInstructionAuditEvent(
                        ZMachineInstructionAuditEventKind.InstructionRecorded,
                        group.Number,
                        null,
                        null,
                        instruction)))
                {
                    break;
                }
            }
        }

        _writerTask = Task.Run(WriteLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _audit.EventRecorded -= OnAuditEvent;
        _events.Writer.TryComplete();
        if (_writerTask is not null)
        {
            await _writerTask.ConfigureAwait(false);
        }
    }

    private void OnAuditEvent(ZMachineInstructionAuditEvent auditEvent) => TryQueue(auditEvent);

    private bool TryQueue(ZMachineInstructionAuditEvent auditEvent)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _failed) != 0)
        {
            return false;
        }

        if (_events.Writer.TryWrite(auditEvent))
        {
            return true;
        }

        Fail(new IOException("The automatic instruction audit queue is full; audit persistence has stopped."), notifyAsync: true);
        return false;
    }

    private async Task WriteLoopAsync()
    {
        var groups = new List<MutableGroup>();
        var groupsByNumber = new Dictionary<long, MutableGroup>();
        var groupAliases = new Dictionary<long, long>();
        try
        {
            Directory.CreateDirectory(_directory);
            var path = _basePath + ".jsonl";
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            await foreach (var auditEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var line = ApplyEvent(auditEvent, groups, groupsByNumber, groupAliases);
                if (line is not null)
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }

            await writer.FlushAsync().ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            if (Volatile.Read(ref _failed) == 0)
            {
                await InstructionAuditExporter.WritePdfAsync(PdfPath!, _storyName, Snapshot(groups)).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static string? ApplyEvent(
        ZMachineInstructionAuditEvent auditEvent,
        List<MutableGroup> groups,
        Dictionary<long, MutableGroup> groupsByNumber,
        Dictionary<long, long> groupAliases)
    {
        if (auditEvent.Kind == ZMachineInstructionAuditEventKind.HistoryCleared)
        {
            if (auditEvent.RelatedGroupNumber is not { } previousGroup
                || !groupsByNumber.ContainsKey(ResolveAlias(previousGroup, groupAliases)))
            {
                throw new InvalidDataException($"Invalid history-clear event for group {auditEvent.GroupNumber}.");
            }

            groupAliases[auditEvent.GroupNumber] = ResolveAlias(previousGroup, groupAliases);
            return null;
        }

        if (auditEvent.Kind == ZMachineInstructionAuditEventKind.CommandStarted)
        {
            if (auditEvent.Command is null || auditEvent.StartedAt is null || groupsByNumber.ContainsKey(auditEvent.GroupNumber))
            {
                throw new InvalidDataException($"Invalid command group event {auditEvent.GroupNumber}.");
            }

            var group = new MutableGroup(auditEvent.GroupNumber, auditEvent.Command, auditEvent.StartedAt.Value);
            groups.Add(group);
            groupsByNumber.Add(group.Number, group);
            return InstructionAuditExporter.SerializeCommand(new ZMachineInstructionAuditGroup(
                group.Number,
                group.Command,
                group.StartedAt,
                ImmutableArray<ZMachineInstructionAuditEntry>.Empty));
        }

        if (auditEvent.Kind != ZMachineInstructionAuditEventKind.InstructionRecorded
            || auditEvent.Instruction is null
            || !groupsByNumber.TryGetValue(ResolveAlias(auditEvent.GroupNumber, groupAliases), out var instructionGroup))
        {
            throw new InvalidDataException($"Invalid instruction event for group {auditEvent.GroupNumber}.");
        }

        instructionGroup.Instructions.Add(auditEvent.Instruction);
        return InstructionAuditExporter.SerializeInstruction(instructionGroup.Number, auditEvent.Instruction);
    }

    private static long ResolveAlias(long groupNumber, Dictionary<long, long> groupAliases)
    {
        while (groupAliases.TryGetValue(groupNumber, out var alias))
        {
            groupNumber = alias;
        }

        return groupNumber;
    }

    private static ImmutableArray<ZMachineInstructionAuditGroup> Snapshot(List<MutableGroup> groups) => groups
        .Select(group => new ZMachineInstructionAuditGroup(
            group.Number,
            group.Command,
            group.StartedAt,
            group.Instructions.ToImmutableArray()))
        .ToImmutableArray();

    private string CreateUniqueBasePath()
    {
        var safeName = string.Concat(_storyName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "game";
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(_directory, $"{safeName}-audit-{timestamp}-{suffix}");
    }

    private void Fail(Exception exception, bool notifyAsync = false)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
        {
            return;
        }

        _audit.EventRecorded -= OnAuditEvent;
        _events.Writer.TryComplete();
        try
        {
            if (notifyAsync)
            {
                _ = Task.Run(() => NotifyFailure(exception));
            }
            else
            {
                NotifyFailure(exception);
            }
        }
        catch
        {
            // Persistence reporting must not interrupt game execution.
        }
    }

    private void NotifyFailure(Exception exception)
    {
        try
        {
            _writeFailed(exception);
        }
        catch
        {
            // Persistence reporting must not interrupt game execution.
        }
    }

    private sealed class MutableGroup(long number, string command, DateTimeOffset startedAt)
    {
        public long Number { get; } = number;

        public string Command { get; } = command;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public List<ZMachineInstructionAuditEntry> Instructions { get; } = [];
    }
}
