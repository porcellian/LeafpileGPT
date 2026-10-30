using Avalonia.Threading;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Runs one story on a worker thread and bridges VM services to the desktop display.</summary>
public sealed class DesktopGameSession : IAsyncDisposable
{
    private readonly GameDisplayModel _display;
    private readonly Action<ZMachineScreenOperation> _screenOperationWriter;
    private readonly DesktopInputProvider _input;
    private readonly DesktopTranscriptWriter _transcript;
    private readonly ZMachineRuntimeErrorManager _runtimeErrors;
    private readonly ZMachineGameIdentity _gameIdentity;
    private readonly StoryFile _story;
    private readonly string _storyPath;
    private readonly BlorbFile? _blorb;
    private readonly Action<Action> _dispatch;
    private readonly InstructionAuditAutoSaver? _instructionAuditAutoSaver;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _runTask;
    private CancellationTokenSource? _activeInstruction;
    private TaskCompletionSource? _pendingRestart;
    private PendingRestore? _pendingRestore;
    private uint _activeInstructionProgramCounter;
    private int _restartRequested;
    private int _disposed;
    private ZMachineRuntimeErrorRecord? _terminalError;

    public DesktopGameSession(
        StoryFile story,
        string storyPath,
        GameDisplayModel display,
        Action<ZMachineScreenOperation> screenOperationWriter,
        string saveDirectory,
        string transcriptDirectory,
        BlorbFile? blorb = null,
        Action<Action>? dispatch = null,
        ZMachineRuntimeErrorManager? runtimeErrors = null,
        string? auditsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyPath);
        _story = story;
        _storyPath = Path.GetFullPath(storyPath);
        _blorb = blorb;
        _display = display ?? throw new ArgumentNullException(nameof(display));
        _screenOperationWriter = screenOperationWriter ?? throw new ArgumentNullException(nameof(screenOperationWriter));
        ArgumentException.ThrowIfNullOrWhiteSpace(saveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(transcriptDirectory);
        _dispatch = dispatch ?? DispatchToUi;
        _runtimeErrors = runtimeErrors ?? new ZMachineRuntimeErrorManager(NullZMachineRuntimeLog.Instance);
        _gameIdentity = new ZMachineGameIdentity(
            Path.GetFullPath(storyPath),
            story.Header.Version,
            story.Header.ReleaseNumber,
            story.Header.SerialNumber);

        var storyName = Path.GetFileNameWithoutExtension(storyPath);
        SaveStore = new DesktopZMachineSaveStore(saveDirectory, storyName);
        _transcript = new DesktopTranscriptWriter(transcriptDirectory, storyName);
        _transcript.WriteFailed += exception => Dispatch(() => TranscriptWriteFailed?.Invoke(exception));
        EventServices = new DesktopZMachineEventServices();
        State = ZMachineExecutionState.Start(
            story,
            eventServices: EventServices,
            pictureCatalog: blorb?.Pictures,
            soundCatalog: blorb?.Sounds,
            saveStore: SaveStore);
        EventServices.SoundPlaybackUnavailable += message =>
        {
            _runtimeErrors.ReportHostWarning(
                new ZMachineExecutionException(message),
                _gameIdentity,
                State.ProgramCounter);
        };
        if (auditsDirectory is not null)
        {
            _instructionAuditAutoSaver = new InstructionAuditAutoSaver(
                auditsDirectory,
                storyName,
                InstructionAudit,
                exception => _runtimeErrors.ReportHostWarning(
                    new IOException($"Automatic instruction audit could not be saved: {exception.Message}", exception),
                    _gameIdentity,
                    0));
        }

        _input = new DesktopInputProvider(State.Memory)
        {
            BeforeInputWaitAsync = ReleaseStateForInputAsync,
            AfterInputWaitAsync = ReacquireStateAfterInputAsync
        };
        _input.LineInputRequested += request => InputRequested?.Invoke(request);
        _input.CharacterInputRequested += () => CharacterInputRequested?.Invoke();
        _input.InputConsumed += InstructionAudit.BeginCommand;
        State.OutputStreams.ScreenWriter = text =>
        {
            var window = State.OutputStreams.Screen.SelectedWindow;
            Dispatch(() => _display.AppendOutput(text, window));
        };
        State.OutputStreams.TranscriptWriter = _transcript.Write;
        State.OutputStreams.Screen.StatusLineWriter = status =>
            Dispatch(() => _display.SetStatusLine(status.Location, status.RightText));
        State.OutputStreams.Screen.OperationWriter = operation => Dispatch(() => _screenOperationWriter(operation));
    }

    public ZMachineExecutionState State { get; }

    public IZMachineSaveStore SaveStore { get; }

    public DesktopZMachineEventServices EventServices { get; }

    public ZMachineInstructionAudit InstructionAudit { get; } = new();

    public event Action<Exception>? TranscriptWriteFailed;

    public event Action<ZMachineLineInputRequest>? InputRequested;

    public event Action? CharacterInputRequested;

    public Task Completion => _runTask ?? Task.CompletedTask;

    public string? TranscriptPath => _transcript.FilePath;

    public ZMachineRuntimeErrorRecord? TerminalError => _terminalError;

    public async Task<StoryFileInfoSnapshot> CaptureStoryFileInfoSnapshotAsync(CancellationToken cancellationToken = default)
    {
        System.Collections.Immutable.ImmutableArray<byte> runtimeHeader;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            runtimeHeader = State.CaptureRuntimeHeaderSnapshot();
        }
        finally
        {
            _stateGate.Release();
        }

        return StoryFileInfoSnapshot.Create(_storyPath, _story, runtimeHeader, _blorb);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_runTask is not null)
        {
            throw new InvalidOperationException("This story session has already been started.");
        }

        _instructionAuditAutoSaver?.Start();
        _runTask = Task.Run(() => RunLoopAsync(_shutdown.Token));
    }

    public bool SubmitCommand(string command) => _input.SubmitLine(command);

    public async Task SetScreenDimensionsAsync(
        int widthInCharacters,
        int heightInLines,
        int morePageLineLimit,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            State.OutputStreams.Screen.SetDimensions(widthInCharacters, heightInLines);
            State.OutputStreams.Screen.SetMorePageLineLimit(morePageLineLimit);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public bool SubmitMoreKey() => _input.SubmitMoreKey();

    public async Task<QuetzalSaveData> CaptureSaveStateAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            uint? savedPc = Volatile.Read(ref _activeInstruction) is null
                ? null
                : Volatile.Read(ref _activeInstructionProgramCounter);
            return State.CaptureSaveState(savedPc);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task ApplySaveStateAsync(QuetzalSaveData save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        TaskCompletionSource? completion = null;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _activeInstruction) is null)
            {
                InstructionAudit.BeginCommand("Restore");
                State.ApplySaveState(save);
                Dispatch(_display.ResetAfterRestore);
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _pendingRestore, new PendingRestore(save, completion));
                Volatile.Read(ref _activeInstruction)?.Cancel();
            }
        }
        finally
        {
            _stateGate.Release();
        }

        if (completion is not null)
        {
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_runTask?.IsCompleted == true)
        {
            _runTask = null;
            Start();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_runTask is null || _runTask.IsCompleted)
        {
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                InstructionAudit.BeginCommand("Restart");
                State.Restart();
                Dispatch(ResetDisplay);
            }
            finally
            {
                _stateGate.Release();
            }

            if (_runTask is not null)
            {
                _runTask = null;
                Start();
            }

            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pendingRestart, completion);
        Interlocked.Exchange(ref _restartRequested, 1);
        Volatile.Read(ref _activeInstruction)?.Cancel();
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        Volatile.Read(ref _activeInstruction)?.Cancel();
        Volatile.Read(ref _pendingRestart)?.TrySetCanceled();
        Interlocked.Exchange(ref _pendingRestore, null)?.Completion.TrySetCanceled();
        _input.Complete();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
        }

        _transcript.Dispose();
        EventServices.Dispose();
        if (_instructionAuditAutoSaver is not null)
        {
            await _instructionAuditAutoSaver.DisposeAsync().ConfigureAwait(false);
        }

        _shutdown.Dispose();
        _stateGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var decoder = new ZMachineInstructionDecoder(State.Memory);
        var executor = new ZMachineOpcodeExecutor();
        var instructionCount = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var restore = Interlocked.Exchange(ref _pendingRestore, null);
                    if (restore is not null)
                    {
                        InstructionAudit.BeginCommand("Restore");
                        State.ApplySaveState(restore.Save);
                        Dispatch(_display.ResetAfterRestore);
                        restore.Completion.TrySetResult();
                        continue;
                    }

                    if (Interlocked.Exchange(ref _restartRequested, 0) != 0)
                    {
                        InstructionAudit.BeginCommand("Restart");
                        State.Restart();
                        Dispatch(ResetDisplay);
                        Volatile.Read(ref _pendingRestart)?.TrySetResult();
                        Volatile.Write(ref _pendingRestart, null);
                    }

                    if (State.IsHalted)
                    {
                        return;
                    }

                    using var instructionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Volatile.Write(ref _activeInstruction, instructionCancellation);
                    DecodedInstruction? instruction = null;
                    var instructionRecorded = false;
                    var instructionPc = State.ProgramCounter;
                    try
                    {
                        Volatile.Write(ref _activeInstructionProgramCounter, instructionPc);
                        instruction = decoder.Decode(instructionPc);
                        await executor.ExecuteAsync(instruction, State, _input, cancellationToken: instructionCancellation.Token)
                            .ConfigureAwait(false);
                        InstructionAudit.Record(instruction, State);
                        instructionRecorded = true;
                        await State.OutputStreams.Screen.WaitForMoreAsync(
                            _input.ReadMoreKeyAsync,
                            instructionCancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (Volatile.Read(ref _restartRequested) != 0 || Volatile.Read(ref _pendingRestore) is not null)
                    {
                        // The next loop pass restarts at the story's initial execution point.
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var fatal = State.IsHalted
                            || instruction is null
                            || exception is ZMachineExecutionException { IsFatal: true }
                            || exception is not (ZMachineExecutionException or ZMachineMemoryAccessException);
                        var diagnostic = _runtimeErrors.Report(
                            exception,
                            _gameIdentity,
                            instructionPc,
                            instruction,
                            State.CallStack,
                            State.EvaluationStack,
                            fatal);
                        if (diagnostic.ShouldStop)
                        {
                            _terminalError = diagnostic;
                            State.Halt();
                            return;
                        }

                        State.SetProgramCounter(instruction!.NextProgramCounter);
                        if (!instructionRecorded)
                        {
                            InstructionAudit.Record(instruction, State);
                        }

                        await State.OutputStreams.Screen.WaitForMoreAsync(
                            _input.ReadMoreKeyAsync,
                            instructionCancellation.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Volatile.Write(ref _activeInstruction, null);
                        Volatile.Write(ref _activeInstructionProgramCounter, 0);
                    }
                }
                finally
                {
                    _stateGate.Release();
                }

                if (++instructionCount % 256 == 0)
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var diagnostic = _runtimeErrors.Report(
                exception,
                _gameIdentity,
                State.ProgramCounter,
                null,
                State.CallStack,
                State.EvaluationStack,
                fatal: true);
            _terminalError = diagnostic;
            State.Halt();
        }
    }

    private ValueTask ReleaseStateForInputAsync()
    {
        _stateGate.Release();
        return ValueTask.CompletedTask;
    }

    private async ValueTask ReacquireStateAfterInputAsync() =>
        await _stateGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

    private void ResetDisplay()
    {
        _display.ResetPresentation();
        _screenOperationWriter(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearScreen));
    }

    private void Dispatch(Action action) => _dispatch(action);

    private static void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private sealed record PendingRestore(QuetzalSaveData Save, TaskCompletionSource Completion);
}
