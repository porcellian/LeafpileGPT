using System.Threading.Channels;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Bridges submitted desktop prompt lines into asynchronous Z-machine keyboard reads.</summary>
public sealed class DesktopInputProvider : IZMachineInputProvider
{
    private readonly Channel<string> _input = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Channel<ushort> _moreKeys = Channel.CreateUnbounded<ushort>();
    private readonly ZsciiCodec _zscii;

    public DesktopInputProvider(ZMachineMemory memory) =>
        _zscii = new ZsciiCodec(memory ?? throw new ArgumentNullException(nameof(memory)));

    public bool SupportsTimedInput => false;

    public Func<ValueTask>? BeforeInputWaitAsync { get; set; }

    public Func<ValueTask>? AfterInputWaitAsync { get; set; }

    public event Action<ZMachineLineInputRequest>? LineInputRequested;

    /// <summary>Raised when a submitted line has been consumed by a line-input instruction.</summary>
    public event Action<string>? InputConsumed;

    /// <summary>Raised when the interpreter is waiting for a single character.</summary>
    public event Action? CharacterInputRequested;

    /// <summary>Queues a submitted line, including an empty line, for the next VM input request.</summary>
    public bool SubmitLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return _input.Writer.TryWrite(line);
    }

    /// <summary>Queues a key to continue output paused at a MORE prompt.</summary>
    public bool SubmitMoreKey(ushort key = 32) => _moreKeys.Writer.TryWrite(key);

    public async ValueTask<ZMachineLineInputResult> ReadLineAsync(
        ZMachineLineInputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var line = await ReadSubmittedLineAsync(
            cancellationToken,
            () => LineInputRequested?.Invoke(request)).ConfigureAwait(false);
        InputConsumed?.Invoke(line);
        return new ZMachineLineInputResult(line, 13);
    }

    public async ValueTask<ushort> ReadCharacterAsync(CancellationToken cancellationToken = default)
    {
        var line = await ReadSubmittedLineAsync(
            cancellationToken,
            () => CharacterInputRequested?.Invoke()).ConfigureAwait(false);
        if (line.Length == 0)
        {
            InputConsumed?.Invoke("[Return]");
            return 13;
        }

        if (!_zscii.TryEncodeInput(line[0], out var character))
        {
            throw new InvalidOperationException($"Character U+{(int)line[0]:X4} cannot be entered by the Z-machine keyboard.");
        }

        InputConsumed?.Invoke(line[0].ToString());
        return character;
    }

    public async ValueTask<ushort> ReadMoreKeyAsync(CancellationToken cancellationToken = default)
    {
        if (BeforeInputWaitAsync is not null)
        {
            await BeforeInputWaitAsync().ConfigureAwait(false);
        }

        try
        {
            return await _moreKeys.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (AfterInputWaitAsync is not null)
            {
                await AfterInputWaitAsync().ConfigureAwait(false);
            }
        }
    }

    public void Complete()
    {
        _input.Writer.TryComplete();
        _moreKeys.Writer.TryComplete();
    }

    private async ValueTask<string> ReadSubmittedLineAsync(
        CancellationToken cancellationToken,
        Action? onWaiting = null)
    {
        if (BeforeInputWaitAsync is not null)
        {
            await BeforeInputWaitAsync().ConfigureAwait(false);
        }

        try
        {
            onWaiting?.Invoke();
            return await _input.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (AfterInputWaitAsync is not null)
            {
                await AfterInputWaitAsync().ConfigureAwait(false);
            }
        }
    }
}
