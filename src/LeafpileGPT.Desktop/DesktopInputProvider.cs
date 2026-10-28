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
    private readonly ZsciiCodec _zscii;

    public DesktopInputProvider(ZMachineMemory memory) =>
        _zscii = new ZsciiCodec(memory ?? throw new ArgumentNullException(nameof(memory)));

    public bool SupportsTimedInput => false;

    public Func<ValueTask>? BeforeInputWaitAsync { get; set; }

    public Func<ValueTask>? AfterInputWaitAsync { get; set; }

    public event Action<ZMachineLineInputRequest>? LineInputRequested;

    /// <summary>Queues a submitted line, including an empty line, for the next VM input request.</summary>
    public bool SubmitLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return _input.Writer.TryWrite(line);
    }

    public async ValueTask<ZMachineLineInputResult> ReadLineAsync(
        ZMachineLineInputRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var line = await ReadSubmittedLineAsync(
            cancellationToken,
            () => LineInputRequested?.Invoke(request)).ConfigureAwait(false);
        return new ZMachineLineInputResult(line, 13);
    }

    public async ValueTask<ushort> ReadCharacterAsync(CancellationToken cancellationToken = default)
    {
        var line = await ReadSubmittedLineAsync(cancellationToken).ConfigureAwait(false);
        if (line.Length == 0)
        {
            return 13;
        }

        if (!_zscii.TryEncodeInput(line[0], out var character))
        {
            throw new InvalidOperationException($"Character U+{(int)line[0]:X4} cannot be entered by the Z-machine keyboard.");
        }

        return character;
    }

    public void Complete() => _input.Writer.TryComplete();

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
