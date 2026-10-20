namespace Leafpile.Core.Machine;

/// <summary>Routes printed text through the selected Z-machine output streams.</summary>
public sealed class ZMachineOutputStreams
{
    private const int MaximumMemoryStreamDepth = 16;
    private readonly ZMachineState _state;
    private readonly IZMachineInputOutput? _host;
    private readonly Stack<MemoryStreamFrame> _memoryStreams = new();
    private bool _screenSelected = true;
    private bool _transcriptSelected;
    private bool _commandSelected;
    private bool _bufferingEnabled = true;
    private bool _transcriptWarningReported;
    private bool _commandWarningReported;

    public ZMachineOutputStreams(ZMachineState state, IZMachineInputOutput? host)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _host = host;
        _transcriptSelected = (state.Memory.ReadWord(0x10) & 1) != 0;
    }

    public bool IsCommandStreamSelected => _commandSelected;

    public bool BufferingEnabled => _bufferingEnabled;

    public void SetBuffering(bool enabled) => _bufferingEnabled = enabled;

    public string Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _transcriptSelected = (_state.Memory.ReadWord(0x10) & 1) != 0;

        if (_memoryStreams.Count != 0)
        {
            foreach (var character in text.EnumerateRunes())
            {
                WriteMemoryCharacter(character);
            }

            return string.Empty;
        }

        if (_transcriptSelected && _state.Screen.CopyTextToTranscript && _host?.SupportsTranscript == true)
        {
            _host?.WriteTranscript(text);
        }

        return _screenSelected ? text : string.Empty;
    }

    public string WriteInputEcho(string text, bool allowTranscript)
    {
        _transcriptSelected = (_state.Memory.ReadWord(0x10) & 1) != 0;
        if (_memoryStreams.Count != 0)
        {
            return string.Empty;
        }

        if (_transcriptSelected && allowTranscript && _state.Screen.CopyTextToTranscript && _host?.SupportsTranscript == true)
        {
            _host?.WriteTranscript(text);
        }

        return _screenSelected ? text : string.Empty;
    }

    public void SelectOutput(short stream, ushort? table = null, short? width = null)
    {
        if (stream == 0)
        {
            return;
        }

        var selected = stream > 0;
        switch (Math.Abs((int)stream))
        {
            case 1:
                _screenSelected = selected;
                break;
            case 2:
                SetTranscriptSelected(selected);
                break;
            case 3 when selected:
                if (!table.HasValue)
                {
                    throw new ZMachineRuntimeException("Selecting output stream 3 requires a table address.");
                }

                if (_memoryStreams.Count == MaximumMemoryStreamDepth)
                {
                    throw new ZMachineRuntimeException("Output stream 3 nesting exceeds the supported depth of 16.");
                }

                _state.Memory.ValidateDynamicRange(table.Value, sizeof(ushort));
                var formattedWidth = _state.Header.Version == 6 && width.HasValue
                    ? _state.Screen.GetFormattedStreamWidth(width.Value)
                    : 0;
                _memoryStreams.Push(new MemoryStreamFrame(table.Value, 0, formattedWidth, new List<byte>()));
                break;
            case 3:
                if (_memoryStreams.Count == 0)
                {
                    throw new ZMachineRuntimeException("Output stream 3 cannot be deselected because it is not selected.");
                }

                var frame = _memoryStreams.Pop();
                if (frame.FormattedWidth > 0)
                {
                    WriteFormattedFrame(frame);
                }
                else
                {
                    _state.Memory.WriteWord(frame.TableAddress, frame.CharacterCount);
                }
                break;
            case 4:
                _commandSelected = selected;
                if (selected && _host is not null && !_host.SupportsCommandRecording && !_commandWarningReported)
                {
                    _host.ReportFileAccessUnavailable("command recording");
                    _commandWarningReported = true;
                }

                break;
            default:
                throw new ZMachineRuntimeException($"Output stream {stream} is not defined.");
        }
    }

    public void SetTranscriptSelected(bool selected)
    {
        _transcriptSelected = selected;
        var flags = _state.Memory.ReadWord(0x10);
        _state.Memory.WriteWord(0x10, selected ? (ushort)(flags | 1) : (ushort)(flags & ~1));
        if (selected && _host is not null && !_host.SupportsTranscript && !_transcriptWarningReported)
        {
            _host.ReportFileAccessUnavailable("game transcript");
            _transcriptWarningReported = true;
        }
    }

    public void RecordCommand(string command)
    {
        if (_commandSelected && _host?.SupportsCommandRecording == true)
        {
            _host.WriteCommand(command);
        }
    }

    public void RecordKeypress(byte zsciiCode)
    {
        if (_commandSelected && _host?.SupportsCommandRecording == true)
        {
            _host.WriteKeypress(zsciiCode);
        }
    }

    private void WriteMemoryCharacter(System.Text.Rune character)
    {
        var zscii = character.Value == '\n'
            ? (byte)13
            : character.IsBmp && _state.Characters.TryGetZsciiForUnicode((char)character.Value, out var code) ? code : (byte)'?';
        var frame = _memoryStreams.Pop();
        if (frame.FormattedWidth > 0)
        {
            frame.FormattedBytes!.Add(zscii);
            _memoryStreams.Push(frame);
            return;
        }

        var count = checked((ushort)(frame.CharacterCount + 1));
        var address = (long)frame.TableAddress + sizeof(ushort) + frame.CharacterCount;
        if (address > ushort.MaxValue)
        {
            throw new ZMachineRuntimeException("Output stream 3 table exceeds the addressable memory range.");
        }

        _state.Memory.ValidateDynamicRange(checked((ushort)address), sizeof(byte));
        _state.Memory.WriteByte((ushort)address, zscii);
        _memoryStreams.Push(frame with { CharacterCount = count });
    }

    private void WriteFormattedFrame(MemoryStreamFrame frame)
    {
        var bytes = frame.FormattedBytes!;
        var lines = new List<List<byte>> { new() };
        foreach (var value in bytes)
        {
            if (value == 13)
            {
                lines.Add(new List<byte>());
                continue;
            }

            var line = lines[^1];
            if (line.Count >= frame.FormattedWidth)
            {
                var lastSpace = line.FindLastIndex(value => value == (byte)' ');
                if (lastSpace >= 0)
                {
                    var remainder = line.Skip(lastSpace + 1).ToList();
                    line.RemoveRange(lastSpace, line.Count - lastSpace);
                    lines.Add(remainder);
                }
                else
                {
                    lines.Add(new List<byte>());
                }

                line = lines[^1];
            }
            line.Add(value);
        }

        var address = (int)frame.TableAddress;
        foreach (var line in lines)
        {
            if (line.Count == 0) continue;
            ValidateFormattedAddress(address, sizeof(ushort));
            _state.Memory.WriteWord((ushort)address, checked((ushort)line.Count));
            address += sizeof(ushort);
            foreach (var value in line)
            {
                ValidateFormattedAddress(address, sizeof(byte));
                _state.Memory.WriteByte((ushort)address++, value);
            }
        }

        ValidateFormattedAddress(address, sizeof(ushort));
        _state.Memory.WriteWord((ushort)address, 0);
    }

    private void ValidateFormattedAddress(int address, int width)
    {
        if (address < 0 || address > ushort.MaxValue)
            throw new ZMachineRuntimeException("Formatted output stream table exceeds the Z-machine address space.");
        _state.Memory.ValidateDynamicRange((ushort)address, width);
    }

    private sealed record MemoryStreamFrame(ushort TableAddress, ushort CharacterCount, int FormattedWidth, List<byte>? FormattedBytes);
}
