namespace LeafpileGPT.Core;
/// <summary>Routes Z-machine text to the selected screen, transcript, memory, and command streams.</summary>
public sealed class ZMachineOutputStreams
{
    private const ushort Flags2Address = 0x10;
    private const ushort Stream3WidthAddress = 0x30;
    private const ushort TranscriptFlag = 0x0001;
    private readonly ZMachineMemory _memory;
    private readonly List<MemoryOutputScope> _memoryScopes = [];
    private ZsciiCodec? _zscii;
    private bool _screenSelected = true;
    private bool _transcriptSelected;
    private bool _commandStreamSelected;
    private ushort _lastFlags2;

    public ZMachineOutputStreams(ZMachineMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Screen = new ZMachineScreenModel(memory);
        _lastFlags2 = memory.ReadWord(Flags2Address);
        _transcriptSelected = (_lastFlags2 & TranscriptFlag) != 0;
        BufferingEnabled = true;
    }

    /// <summary>Gets or sets the sink for text routed to stream 1.</summary>
    public Action<string>? ScreenWriter { get; set; }

    /// <summary>Gets or sets the sink for text routed to stream 2.</summary>
    public Action<string>? TranscriptWriter { get; set; }

    /// <summary>Gets or sets the sink for completed commands routed to stream 4.</summary>
    public Action<string>? CommandWriter { get; set; }

    /// <summary>Gets or sets the sink for individual keypresses routed to stream 4.</summary>
    public Action<byte>? KeyPressWriter { get; set; }

    public ZMachineScreenModel Screen { get; }

    /// <summary>Raised when stream 1 buffering changes.</summary>
    public event Action<bool>? BufferingChanged;

    /// <summary>Optionally measures one Unicode scalar in V6 output units; defaults to one unit.</summary>
    public Func<int, int>? MeasureCharacterWidth { get; set; }

    public bool ScreenSelected => _screenSelected;

    public bool TranscriptSelected => _transcriptSelected;

    public bool CommandStreamSelected => _commandStreamSelected;

    public bool BufferingEnabled { get; private set; }

    public int MemoryStreamDepth => _memoryScopes.Count;

    public ushort? CurrentMemoryTableAddress => _memoryScopes.Count == 0 ? null : _memoryScopes[^1].TableAddress;

    /// <summary>Gets the optional formatted-output width supplied when a V6 memory stream was selected.</summary>
    public short? CurrentMemoryStreamWidth => _memoryScopes.Count == 0 ? null : _memoryScopes[^1].FormattingWidth;

    /// <summary>Selects or deselects streams 1 through 4 according to output_stream semantics.</summary>
    public void SelectStream(short streamNumber, ushort? tableAddress = null, short? formattingWidth = null)
    {
        if (streamNumber == 0)
        {
            return;
        }

        var select = streamNumber > 0;
        switch (Math.Abs((int)streamNumber))
        {
            case 1 when _memory.Header.Version >= 3:
                _screenSelected = select;
                break;
            case 2 when _memory.Header.Version >= 3:
                SetTranscriptSelected(select);
                break;
            case 3 when _memory.Header.Version >= 3 && select:
                if (tableAddress is null or 0)
                {
                    throw new ZMachineExecutionException("Selecting output stream 3 requires a non-zero table address.");
                }

                if (_memoryScopes.Count == 16)
                {
                    throw new ZMachineExecutionException("Output stream 3 cannot be nested more than 16 times.");
                }

                _memory.ValidateWritableRange(tableAddress.Value, sizeof(ushort));
                _memoryScopes.Add(new MemoryOutputScope(tableAddress.Value, formattingWidth));
                break;
            case 3 when _memory.Header.Version >= 3:
                CloseMemoryStream();
                break;
            case 4 when _memory.Header.Version >= 3:
                _commandStreamSelected = select;
                break;
            default:
                throw new ZMachineExecutionException(
                    $"Output stream {streamNumber} is not defined for Z-machine version {_memory.Header.Version}.");
        }
    }

    /// <summary>Sets the buffering mode for versions where buffer_mode is defined.</summary>
    public void SetBuffering(bool enabled)
    {
        var version = _memory.Header.Version;
        if (version is < 4 or > 6 || (version != 6 && BufferingEnabled == enabled))
        {
            return;
        }

        BufferingEnabled = enabled;
        Screen.SetBuffering(enabled);
        BufferingChanged?.Invoke(enabled);
    }

    /// <summary>Writes decoded Unicode text through the currently selected streams.</summary>
    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SynchronizeTranscriptSelection();
        if (_memoryScopes.Count > 0)
        {
            WriteMemoryText(text);
            return;
        }

        if (_screenSelected)
        {
            Screen.WriteText(text, ScreenWriter);
        }

        if (_transcriptSelected && (_memory.Header.Version <= 5 || Screen.CopiesCurrentWindowToTranscript))
        {
            TranscriptWriter?.Invoke(text);
        }
    }

    /// <summary>Writes one output ZSCII character, preserving its byte value on stream 3.</summary>
    public void WriteZsciiCharacter(byte character, ZsciiCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (character == 0)
        {
            return;
        }

        var decoded = codec.Decode(character);
        if (decoded == '\uFFFD' || decoded == '\0')
        {
            throw new ZMachineExecutionException($"ZSCII character {character} is not defined for output.");
        }

        SynchronizeTranscriptSelection();
        if (_memoryScopes.Count > 0)
        {
            var widthUnits = character == 13 ? 0 : MeasureCharacterWidth?.Invoke(decoded) ?? 1;
            WriteMemoryCharacter(character, widthUnits);
            return;
        }

        var text = character == 13 ? "\n" : decoded.ToString();
        if (_screenSelected)
        {
            Screen.WriteText(text, ScreenWriter);
        }

        if (_transcriptSelected && (_memory.Header.Version <= 5 || Screen.CopiesCurrentWindowToTranscript))
        {
            TranscriptWriter?.Invoke(text);
        }
    }

    /// <summary>Records a completed player command as one stream 4 event.</summary>
    public void RecordCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_commandStreamSelected)
        {
            CommandWriter?.Invoke(command + "\n");
        }
    }

    /// <summary>Records a keypress as a stream 4 event.</summary>
    public void RecordKeyPress(byte zsciiCharacter)
    {
        if (_commandStreamSelected)
        {
            KeyPressWriter?.Invoke(zsciiCharacter);
        }
    }

    /// <summary>Echoes V1–V5 line input through the screen and selected transcript streams.</summary>
    public void EchoInput(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_memoryScopes.Count > 0)
        {
            return;
        }

        SynchronizeTranscriptSelection();
        if (_screenSelected)
        {
            Screen.WriteText(input, ScreenWriter);
        }

        if (_memory.Header.Version <= 5 && _transcriptSelected)
        {
            TranscriptWriter?.Invoke(input);
        }
    }

    private void SynchronizeTranscriptSelection()
    {
        var flags2 = _memory.ReadWord(Flags2Address);
        if (flags2 == _lastFlags2)
        {
            return;
        }

        _lastFlags2 = flags2;
        _transcriptSelected = (flags2 & TranscriptFlag) != 0;
    }

    private void SetTranscriptSelected(bool selected)
    {
        _transcriptSelected = selected;
        var flags2 = _memory.ReadWord(Flags2Address);
        flags2 = selected ? (ushort)(flags2 | TranscriptFlag) : (ushort)(flags2 & ~TranscriptFlag);
        _memory.WriteWord(Flags2Address, flags2);
        _lastFlags2 = flags2;
    }

    private void WriteMemoryText(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == 0)
            {
                continue;
            }

            byte zscii;
            if (rune.Value == '\n')
            {
                zscii = 13;
            }
            else if (rune.IsBmp && GetZsciiCodec().TryEncodeOutput((char)rune.Value, out var encoded))
            {
                zscii = encoded;
            }
            else
            {
                zscii = (byte)'?';
            }

            var widthUnits = rune.Value == '\n' ? 0 : MeasureCharacterWidth?.Invoke(rune.Value) ?? 1;
            WriteMemoryCharacter(zscii, widthUnits);
        }
    }

    private void WriteMemoryCharacter(byte zscii, int widthUnits)
    {
        var scope = _memoryScopes[^1];
        var destination = (uint)scope.TableAddress + sizeof(ushort) + scope.CharacterCount;
        _memory.WriteByte(checked((ushort)destination), zscii);
        scope.CharacterCount++;
        if (_memory.Header.Version == 6)
        {
            scope.WidthUnits = checked(scope.WidthUnits + widthUnits);
        }
    }

    private void CloseMemoryStream()
    {
        if (_memoryScopes.Count == 0)
        {
            throw new ZMachineExecutionException("Output stream 3 is not selected.");
        }

        var scope = _memoryScopes[^1];
        _memory.WriteWord(scope.TableAddress, unchecked((ushort)scope.CharacterCount));
        _memoryScopes.RemoveAt(_memoryScopes.Count - 1);
        if (_memory.Header.Version == 6)
        {
            _memory.WriteWord(Stream3WidthAddress, unchecked((ushort)scope.WidthUnits));
        }
    }

    private ZsciiCodec GetZsciiCodec() => _zscii ??= new ZsciiCodec(_memory);

    private sealed class MemoryOutputScope(ushort tableAddress, short? formattingWidth)
    {
        public ushort TableAddress { get; } = tableAddress;

        public short? FormattingWidth { get; } = formattingWidth;

        public int CharacterCount { get; set; }

        public int WidthUnits { get; set; }
    }
}
