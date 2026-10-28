namespace LeafpileGPT.Core;

using System.Collections.Immutable;

/// <summary>
/// Describes a line read for a host input provider. ExistingText is a stored prefix that must be
/// preserved but not automatically echoed; the provider returns only newly entered text.
/// </summary>
public sealed record ZMachineLineInputRequest(
    byte InputStream,
    string ExistingText,
    ushort MaximumAdditionalCharacters,
    ImmutableArray<byte> TerminatingCharacters,
    bool ShowStatusLineBeforeInput);

/// <summary>Contains the additional text typed and the key which ended a line read.</summary>
public readonly record struct ZMachineLineInputResult(string Text, ushort Terminator);

/// <summary>Supplies keyboard or scripted commands to the VM without depending on Avalonia.</summary>
public interface IZMachineInputProvider
{
    bool SupportsTimedInput { get; }

    ValueTask<ZMachineLineInputResult> ReadLineAsync(
        ZMachineLineInputRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ushort> ReadCharacterAsync(CancellationToken cancellationToken = default);
}

/// <summary>Applies defined ZSCII keyboard characters to an editable input line.</summary>
public sealed class ZMachineInputLineEditor
{
    private readonly ZsciiCodec _zscii;
    private readonly int _maximumLength;
    private readonly List<char> _characters;

    public ZMachineInputLineEditor(ZsciiCodec zscii, int maximumLength, string? initialText = null)
    {
        _zscii = zscii ?? throw new ArgumentNullException(nameof(zscii));
        if (maximumLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        _maximumLength = maximumLength;
        _characters = [];
        if (!string.IsNullOrEmpty(initialText))
        {
            foreach (var character in initialText)
            {
                Append(character);
            }
        }
    }

    public string Text => new(_characters.ToArray());

    public int Length => _characters.Count;

    public bool Append(char character)
    {
        if (_characters.Count >= _maximumLength)
        {
            return false;
        }

        var lower = char.ToLowerInvariant(character);
        if (!_zscii.TryEncodeInput(lower, out var code)
            || !_zscii.IsDefinedInputCode(code)
            || code is 0 or 8 or 13 or 27
            || _zscii.Decode(code) == '\uFFFD')
        {
            return false;
        }

        _characters.Add(lower);
        return true;
    }

    public bool Backspace()
    {
        if (_characters.Count == 0)
        {
            return false;
        }

        _characters.RemoveAt(_characters.Count - 1);
        return true;
    }

    public void Clear() => _characters.Clear();

    /// <summary>Returns a valid line terminator, normalizing line-feed to carriage return.</summary>
    public static bool TryGetLineTerminator(ushort zscii, ImmutableArray<byte> allowed, out byte terminator)
    {
        if (zscii is 10 or 13)
        {
            terminator = 13;
            return true;
        }

        if (zscii <= byte.MaxValue && allowed.Contains((byte)zscii))
        {
            terminator = (byte)zscii;
            return true;
        }

        if (zscii <= byte.MaxValue && allowed.Contains(byte.MaxValue) && IsFunctionKey((byte)zscii))
        {
            terminator = (byte)zscii;
            return true;
        }

        terminator = 0;
        return false;
    }

    private static bool IsFunctionKey(byte code) => code is >= 129 and <= 154 or 252 or 253 or 254;
}
