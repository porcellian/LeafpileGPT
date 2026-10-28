namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>Implements line and character input behavior for decoded Z-machine read opcodes.</summary>
public sealed class ZMachineInputHandler
{
    private readonly ZMachineExecutionState _state;
    private readonly IZMachineInputProvider _provider;
    private readonly Func<ushort, CancellationToken, ValueTask<bool>>? _timedRoutine;
    private readonly Action? _redrawInput;
    private readonly ZsciiCodec _zscii;

    public ZMachineInputHandler(
        ZMachineExecutionState state,
        IZMachineInputProvider provider,
        Func<ushort, CancellationToken, ValueTask<bool>>? timedRoutine = null,
        Action? redrawInput = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timedRoutine = timedRoutine;
        _redrawInput = redrawInput;
        _zscii = new ZsciiCodec(state.Memory);
        _state.SetTimedInputSupported(provider.SupportsTimedInput && timedRoutine is not null);
    }

    /// <summary>Reads a command, updates its text and parse buffers, and returns its terminator in V5+.</summary>
    public async ValueTask<byte?> ReadLineAsync(
        ushort textBufferAddress,
        ushort parseBufferAddress,
        ushort time = 0,
        ushort routine = 0,
        CancellationToken cancellationToken = default)
    {
        var memory = _state.Memory;
        var version = _state.Version;
        var maximumLength = memory.ReadByte(textBufferAddress);
        var capacity = maximumLength;
        var requiredTextBufferLength = Math.Max(3, checked(2 + capacity));
        memory.ValidateWritableRange(textBufferAddress, requiredTextBufferLength);
        var existingTextBytes = version >= 5
            ? ZMachineTextBuffer.ReadText(memory, textBufferAddress)
            : ImmutableArray<byte>.Empty;
        if (existingTextBytes.Length > capacity)
        {
            throw new ZMachineExecutionException("The existing input text exceeds the text buffer capacity.");
        }

        var existingText = DecodeInput(existingTextBytes);
        var maximumAdditionalCharacters = checked((ushort)(capacity - existingTextBytes.Length));
        if (parseBufferAddress == 0 && version <= 4)
        {
            throw new ZMachineExecutionException("Versions 1–4 require a non-zero parse-buffer address for read.");
        }

        if (parseBufferAddress != 0)
        {
            var maximumWords = memory.ReadByte(parseBufferAddress);
            var parseBufferLength = Math.Max(6, checked(2 + (maximumWords * 4)));
            memory.ValidateWritableRange(parseBufferAddress, parseBufferLength);
        }

        var terminators = ReadTerminatingCharacters();
        var request = new ZMachineLineInputRequest(
            _state.CurrentInputStream,
            existingText,
            maximumAdditionalCharacters,
            terminators,
            ShowStatusLineBeforeInput: version <= 3);
        if (_state.CurrentInputStream == 0)
        {
            await _state.OutputStreams.Screen.WaitForMoreAsync(_provider.ReadMoreKeyAsync, cancellationToken);
        }

        _state.OutputStreams.Screen.FlushBeforeInput();

        if (request.ShowStatusLineBeforeInput && _state.CurrentInputStream == 0)
        {
            _state.OutputStreams.Screen.ShowStatusLine(_state);
        }

        var timeoutEnabled = version >= 4
            && _state.CurrentInputStream == 0
            && _provider.SupportsTimedInput
            && _timedRoutine is not null
            && time > 0
            && routine > 0;
        var read = await ReadWithTimeoutAsync(
            token => _provider.ReadLineAsync(request, token),
            timeoutEnabled ? TimeSpan.FromMilliseconds(time * 100d) : null,
            routine,
            cancellationToken);

        byte terminator;
        string typedText;
        if (read.TimedOut)
        {
            ZMachineTextBuffer.ClearInput(memory, textBufferAddress);
            terminator = 0;
            typedText = string.Empty;
        }
        else
        {
            var inputResult = read.Value;
            if (!ZMachineInputLineEditor.TryGetLineTerminator(inputResult.Terminator, terminators, out terminator))
            {
                throw new ZMachineExecutionException(
                    $"Input provider returned undefined or non-terminating character {inputResult.Terminator} for a line read.");
            }

            if (terminator != 13 && !_zscii.IsDefinedInputCode(terminator))
            {
                throw new ZMachineExecutionException($"Input provider returned undefined ZSCII terminator {terminator}.");
            }

            typedText = SanitizeLine(inputResult.Text, maximumAdditionalCharacters);
            _ = ZMachineTextBuffer.StoreInput(memory, _zscii, textBufferAddress, typedText.AsSpan(), terminator);
            _state.OutputStreams.Screen.ResetMorePageForCommand();
        }

        var fullLine = DecodeInput(ZMachineTextBuffer.ReadText(memory, textBufferAddress));
        if (typedText.Length > 0)
        {
            _state.OutputStreams.EchoInput(typedText);
        }

        if (terminator == 13)
        {
            _state.OutputStreams.WriteZsciiCharacter(13, _zscii);
        }

        if (!read.TimedOut)
        {
            _state.OutputStreams.RecordCommand(fullLine);
        }

        if (version <= 4 || parseBufferAddress != 0)
        {
            new ZMachineDictionary(memory).Tokenise(textBufferAddress, parseBufferAddress);
        }

        return version >= 5 ? terminator : null;
    }

    /// <summary>Reads one defined ZSCII key from the keyboard and records it on stream 4.</summary>
    public async ValueTask<ushort> ReadCharacterAsync(
        ushort inputDevice,
        ushort time = 0,
        ushort routine = 0,
        CancellationToken cancellationToken = default)
    {
        if (inputDevice != 1)
        {
            throw new ZMachineExecutionException($"read_char input device {inputDevice} is not the keyboard device 1.");
        }

        await _state.OutputStreams.Screen.WaitForMoreAsync(_provider.ReadMoreKeyAsync, cancellationToken);
        _state.OutputStreams.Screen.FlushBeforeInput();
        var timeoutEnabled = _state.Version >= 4
            && _provider.SupportsTimedInput
            && _timedRoutine is not null
            && time > 0
            && routine > 0;
        var read = await ReadWithTimeoutAsync(
            token => _provider.ReadCharacterAsync(token),
            timeoutEnabled ? TimeSpan.FromMilliseconds(time * 100d) : null,
            routine,
            cancellationToken);
        if (read.TimedOut)
        {
            return 0;
        }

        if (read.Value > byte.MaxValue || !_zscii.IsDefinedInputCode((byte)read.Value))
        {
            throw new ZMachineExecutionException($"Input provider returned undefined ZSCII character {read.Value}.");
        }

        _state.OutputStreams.RecordKeyPress((byte)read.Value);
        return read.Value;
    }

    private async ValueTask<TimedRead<T>> ReadWithTimeoutAsync<T>(
        Func<CancellationToken, ValueTask<T>> readInput,
        TimeSpan? interval,
        ushort routine,
        CancellationToken cancellationToken)
        where T : struct
    {
        using var inputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var inputTask = readInput(inputCancellation.Token).AsTask();
        if (interval is null || _timedRoutine is null)
        {
            return new TimedRead<T>(false, await inputTask);
        }

        try
        {
            using var timer = new PeriodicTimer(interval.Value);
            while (true)
            {
                var tickTask = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(inputTask, tickTask);
                if (completed == inputTask || inputTask.IsCompleted)
                {
                    return new TimedRead<T>(false, await inputTask);
                }

                if (!await tickTask)
                {
                    return new TimedRead<T>(false, await inputTask);
                }

                if (inputTask.IsCompleted)
                {
                    return new TimedRead<T>(false, await inputTask);
                }

                if (await _timedRoutine(routine, cancellationToken))
                {
                    return new TimedRead<T>(true, default);
                }

                _redrawInput?.Invoke();
            }
        }
        finally
        {
            if (!inputTask.IsCompleted)
            {
                inputCancellation.Cancel();
                ObserveInputTask(inputTask);
            }
        }
    }

    private ImmutableArray<byte> ReadTerminatingCharacters()
    {
        var result = ImmutableArray.CreateBuilder<byte>();
        result.Add(13);
        result.Add(10);
        if (_state.Version < 5 || _state.Memory.Header.TerminatingCharactersTableAddress is not { } address || address == 0)
        {
            return result.ToImmutable();
        }

        var cursor = (uint)address;
        while (cursor <= ushort.MaxValue)
        {
            _state.Memory.ValidateReadableRange(cursor, 1);
            var character = _state.Memory.ReadByte(checked((ushort)cursor));
            if (character == 0)
            {
                return result.ToImmutable();
            }

            if (!IsFunctionKey(character) && character != byte.MaxValue)
            {
                throw new ZMachineExecutionException(
                    $"Terminating-character table at 0x{address:X4} contains invalid code {character}.");
            }

            result.Add(character);
            cursor++;
        }

        throw new ZMachineExecutionException($"Terminating-character table at 0x{address:X4} has no zero terminator.");
    }

    private string SanitizeLine(string text, ushort maximumAdditionalCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        var editor = new ZMachineInputLineEditor(_zscii, maximumAdditionalCharacters);
        foreach (var character in text)
        {
            if (editor.Length == maximumAdditionalCharacters)
            {
                break;
            }

            editor.Append(character);
        }

        return editor.Text;
    }

    private string DecodeInput(ImmutableArray<byte> text) =>
        string.Concat(text.Select(character => _zscii.Decode(character)));

    private static bool IsFunctionKey(byte character) => character is >= 129 and <= 154 or 252 or 253 or 254;

    private static void ObserveInputTask(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private readonly record struct TimedRead<T>(bool TimedOut, T Value);
}
