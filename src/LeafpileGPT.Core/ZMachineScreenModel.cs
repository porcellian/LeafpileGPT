namespace LeafpileGPT.Core;

using System.Text;

public enum ZMachineScreenOperationKind
{
    ClearScreen,
    ClearWindow,
    SplitWindow,
    SelectWindow,
    MoveCursor,
    EraseLine,
    ScrollWindow,
    SetTextStyle,
    SetColours,
    MorePrompt,
    MorePromptCleared
}

[Flags]
public enum ZMachineTextStyle
{
    Roman = 0,
    ReverseVideo = 1,
    Bold = 2,
    Italic = 4,
    FixedPitch = 8
}

public readonly record struct ZMachineCursorPosition(int Row, int Column);

public sealed record ZMachineStatusLine(string Location, string RightText, bool IsTimeGame);

public sealed record ZMachineScreenOperation(
    ZMachineScreenOperationKind Kind,
    short Window = 0,
    int Value = 0,
    int Row = 0,
    int Column = 0,
    string? Text = null);

/// <summary>Tracks the V1–V5 screen layout and exposes host-renderable screen operations.</summary>
public sealed class ZMachineScreenModel
{
    private const byte Flags1Address = 0x01;
    private const byte StatusUnavailableFlag = 0x10;
    private const byte UpperWindowAvailableFlag = 0x20;
    private const byte ColoursAvailableFlag = 0x01;
    private readonly ZMachineMemory _memory;
    private readonly Queue<(string Text, Action<string>? Writer)> _queuedText = new();
    private readonly Dictionary<short, CursorState> _cursors = new()
    {
        [0] = new(1, 1),
        [1] = new(1, 1)
    };
    private Action<ZMachineStatusLine>? _statusLineWriter;
    private bool _supportsUpperWindow = true;
    private bool _lowerBufferingEnabled = true;
    private int _linesSinceMorePrompt;
    private Action<ZMachineScreenOperation>? _operationWriter;

    public ZMachineScreenModel(ZMachineMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        var header = memory.Header;
        WidthInCharacters = header.ScreenWidthInCharacters is > 0 ? header.ScreenWidthInCharacters.Value : 80;
        HeightInLines = header.ScreenHeightInLines is > 0 ? header.ScreenHeightInLines.Value : 25;
        WidthInUnits = header.ScreenWidthInUnits is > 0 ? header.ScreenWidthInUnits.Value : (ushort)WidthInCharacters;
        HeightInUnits = header.ScreenHeightInUnits is > 0 ? header.ScreenHeightInUnits.Value : (ushort)HeightInLines;
        WriteDimensionsToHeader();

        if (Version == 3)
        {
            SetFlag(StatusUnavailableFlag, enabled: true);
            SetFlag(UpperWindowAvailableFlag, enabled: _supportsUpperWindow);
        }

        if (Version >= 5)
        {
            SetFlag(ColoursAvailableFlag, enabled: true);
            DefaultForegroundColour = 9;
            DefaultBackgroundColour = 2;
            ForegroundColour = DefaultForegroundColour;
            BackgroundColour = DefaultBackgroundColour;
            _memory.WriteByte(0x2C, DefaultBackgroundColour);
            _memory.WriteByte(0x2D, DefaultForegroundColour);
        }

        var initialRow = Version is <= 4 ? LowerWindowBottom : LowerWindowTop;
        _cursors[0].Set(initialRow, 1);
    }

    public byte Version => _memory.Header.Version;

    public int WidthInCharacters { get; private set; }

    public int HeightInLines { get; private set; }

    public ushort WidthInUnits { get; private set; }

    public ushort HeightInUnits { get; private set; }

    public int UpperWindowHeight { get; private set; }

    public short SelectedWindow { get; private set; }

    public ZMachineTextStyle TextStyle { get; private set; }

    public byte ForegroundColour { get; private set; }

    public byte BackgroundColour { get; private set; }

    public byte DefaultForegroundColour { get; private set; }

    public byte DefaultBackgroundColour { get; private set; }

    public bool IsBufferingEnabled => Version <= 3 || (SelectedWindow == 0 && _lowerBufferingEnabled);

    public bool IsWaitingForMore { get; private set; }

    public bool MorePromptsEnabled { get; private set; } = true;

    public Action<ZMachineScreenOperation>? OperationWriter
    {
        get => _operationWriter;
        set
        {
            _operationWriter = value;
            value?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearScreen));
        }
    }

    public Action<ZMachineStatusLine>? StatusLineWriter
    {
        get => _statusLineWriter;
        set
        {
            _statusLineWriter = value;
            if (Version == 3)
            {
                SetFlag(StatusUnavailableFlag, enabled: value is null);
            }
        }
    }

    public bool SupportsUpperWindow
    {
        get => _supportsUpperWindow;
        set
        {
            _supportsUpperWindow = value;
            if (Version == 3)
            {
                SetFlag(UpperWindowAvailableFlag, value);
            }
        }
    }

    public ZMachineCursorPosition GetCursor(short window) => GetCursorState(window).Position;

    public ZMachineCursorPosition CurrentCursor => GetCursor(SelectedWindow);

    public void SetDimensions(int widthInCharacters, int heightInLines, ushort? widthInUnits = null, ushort? heightInUnits = null)
    {
        if (widthInCharacters is < 1 or > byte.MaxValue || heightInLines is < 1 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(widthInCharacters), "Screen character dimensions must be from 1 through 255.");
        }

        WidthInCharacters = widthInCharacters;
        HeightInLines = heightInLines;
        WidthInUnits = widthInUnits ?? checked((ushort)widthInCharacters);
        HeightInUnits = heightInUnits ?? checked((ushort)heightInLines);
        WriteDimensionsToHeader();
        UpperWindowHeight = Math.Min(UpperWindowHeight, MaximumUpperWindowHeight);
        NormalizeCursorsAfterResize();
    }

    public void SplitWindow(ushort height)
    {
        EnsureSplitWindowSupported();
        if (height > MaximumUpperWindowHeight)
        {
            throw new ZMachineExecutionException(
                $"Upper-window height {height} exceeds the available {MaximumUpperWindowHeight} screen lines.");
        }

        UpperWindowHeight = height;
        var upperCursor = _cursors[1];
        if (upperCursor.Row > UpperWindowHeight || upperCursor.Row < 1)
        {
            upperCursor.Set(1, 1);
        }

        var lowerCursor = _cursors[0];
        if (Version == 4)
        {
            lowerCursor.Set(LowerWindowBottom, lowerCursor.Column);
        }
        else if (Version >= 5 && lowerCursor.Row < LowerWindowTop)
        {
            lowerCursor.Set(LowerWindowTop, 1);
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SplitWindow,
            Window: 1,
            Value: height));
        if (Version == 3)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.ClearWindow,
                Window: 1));
        }
    }

    public void SetWindow(short window)
    {
        EnsureSplitWindowSupported();
        if (window is not 0 and not 1)
        {
            throw new ZMachineExecutionException($"Screen window {window} is not defined for version {Version}.");
        }

        SelectedWindow = window;
        if (window == 1)
        {
            _cursors[1].Set(1, 1);
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SelectWindow,
            Window: window));
    }

    public void SetCursor(ushort row, ushort column)
    {
        if (Version < 4 || SelectedWindow != 1)
        {
            return;
        }

        SetCursor(1, row, column);
    }

    public void SetCursor(short window, ushort row, ushort column)
    {
        EnsureWindowExists(window);
        if (window == 0)
        {
            return;
        }

        if (row == 0 || row > UpperWindowHeight || column == 0 || column > WidthInCharacters)
        {
            throw new ZMachineExecutionException(
                $"Cursor position ({row},{column}) lies outside upper window dimensions {UpperWindowHeight}x{WidthInCharacters}.");
        }

        _cursors[window].Set(row, column);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.MoveCursor,
            Window: window,
            Row: row,
            Column: column));
    }

    public void StoreCursorPosition(ushort arrayAddress)
    {
        if (Version < 4)
        {
            throw new ZMachineExecutionException("get_cursor is not available before version 4.");
        }

        _memory.ValidateWritableRange(arrayAddress, sizeof(ushort) * 2);
        var cursor = CurrentCursor;
        _memory.WriteWord(arrayAddress, checked((ushort)cursor.Row));
        _memory.WriteWord(checked((ushort)(arrayAddress + 2)), checked((ushort)cursor.Column));
    }

    public void EraseWindow(short window)
    {
        if (Version < 4)
        {
            throw new ZMachineExecutionException("erase_window is not available before version 4.");
        }

        if (window == -1)
        {
            UpperWindowHeight = 0;
            SelectedWindow = 0;
            _cursors[0].Set(Version == 4 ? LowerWindowBottom : 1, 1);
            _cursors[1].Set(1, 1);
            _linesSinceMorePrompt = 0;
            IsWaitingForMore = false;
            _queuedText.Clear();
            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearScreen));
            return;
        }

        if (window == -2)
        {
            if (Version < 5)
            {
                throw new ZMachineExecutionException("erase_window -2 is only available in version 5.");
            }

            _cursors[0].Set(1, 1);
            _cursors[1].Set(1, 1);
            _linesSinceMorePrompt = 0;
            IsWaitingForMore = false;
            _queuedText.Clear();
            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearScreen));
            return;
        }

        EnsureWindowExists(window);
        var cursor = _cursors[window];
        cursor.Set(window == 0 && Version == 4 ? LowerWindowBottom : 1, 1);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.ClearWindow,
            Window: window));
    }

    public void EraseLine(ushort value)
    {
        if (Version < 4 || value != 1)
        {
            return;
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.EraseLine,
            Window: SelectedWindow,
            Row: CurrentCursor.Row,
            Column: CurrentCursor.Column));
    }

    public void SetBuffering(bool enabled)
    {
        if (Version is < 4 or > 5)
        {
            return;
        }

        _lowerBufferingEnabled = enabled;
    }

    public void SetTextStyle(ushort style)
    {
        if (Version < 4)
        {
            throw new ZMachineExecutionException("set_text_style is not available before version 4.");
        }

        TextStyle = (ZMachineTextStyle)(style & 0x0F);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetTextStyle,
            Window: SelectedWindow,
            Value: (int)TextStyle));
    }

    public void SetColours(ushort foreground, ushort background)
    {
        if (Version != 5)
        {
            throw new ZMachineExecutionException("set_colour is not available before version 5.");
        }

        ForegroundColour = ResolveColour(foreground, ForegroundColour, DefaultForegroundColour);
        BackgroundColour = ResolveColour(background, BackgroundColour, DefaultBackgroundColour);
        _memory.WriteByte(0x2C, BackgroundColour);
        _memory.WriteByte(0x2D, ForegroundColour);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetColours,
            Window: SelectedWindow,
            Value: (ForegroundColour << 8) | BackgroundColour));
    }

    public void ShowStatusLine(ZMachineExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Version > 3 || StatusLineWriter is null)
        {
            return;
        }

        var globals = _memory.Header.GlobalsTableAddress;
        var locationObject = _memory.ReadWord(globals);
        var valueOne = unchecked((short)_memory.ReadWord(checked((ushort)(globals + 2))));
        var valueTwo = _memory.ReadWord(checked((ushort)(globals + 4)));
        var timeGame = Version == 3 && (_memory.ReadByte(Flags1Address) & 0x02) != 0;
        string location;
        try
        {
            location = locationObject == 0 ? string.Empty : new ZMachineObjectTable(state).GetShortName(locationObject);
        }
        catch (ZMachineExecutionException)
        {
            location = string.Empty;
        }

        var rightText = timeGame
            ? $"{Math.Clamp((int)valueOne, 0, 23):00}:{Math.Clamp((int)valueTwo, 0, 59):00}"
            : $"Score: {valueOne} Moves: {valueTwo}";
        StatusLineWriter(new ZMachineStatusLine(location, rightText, timeGame));
    }

    public void ContinueAfterMorePrompt()
    {
        if (!IsWaitingForMore)
        {
            return;
        }

        IsWaitingForMore = false;
        _linesSinceMorePrompt = 0;
        OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePromptCleared));
        while (!IsWaitingForMore && _queuedText.TryDequeue(out var queued))
        {
            WriteTextImmediately(queued.Text, queued.Writer);
        }
    }

    public void SetMorePromptsEnabled(bool enabled)
    {
        MorePromptsEnabled = enabled;
        if (!enabled && IsWaitingForMore)
        {
            ContinueAfterMorePrompt();
        }
    }

    public async ValueTask WaitForMoreAsync(
        Func<CancellationToken, ValueTask<ushort>> readKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readKey);
        while (IsWaitingForMore)
        {
            _ = await readKey(cancellationToken);
            ContinueAfterMorePrompt();
        }
    }

    /// <summary>Forwards screen text to the host and keeps the VM cursor and MORE state current.</summary>
    public void WriteText(string text, Action<string>? screenWriter)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsWaitingForMore)
        {
            _queuedText.Enqueue((text, screenWriter));
            return;
        }

        WriteTextImmediately(text, screenWriter);
    }

    private void WriteTextImmediately(string text, Action<string>? screenWriter)
    {
        var cursor = _cursors[SelectedWindow];
        var cursorRow = cursor.Row;
        var cursorColumn = cursor.Column;
        var upperWindow = SelectedWindow == 1;
        var outputChunk = new StringBuilder();
        var charactersConsumed = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            charactersConsumed += rune.Utf16SequenceLength;
            outputChunk.Append(rune.ToString());
            if (rune.Value is '\r' or '\n')
            {
                AdvanceLine(upperWindow, ref cursorRow, ref cursorColumn);
            }
            else
            {
                cursorColumn++;
                if (cursorColumn > WidthInCharacters)
                {
                    AdvanceLine(upperWindow, ref cursorRow, ref cursorColumn);
                }
            }

            if (IsWaitingForMore)
            {
                screenWriter?.Invoke(outputChunk.ToString());
                OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePrompt));
                cursor.Set(cursorRow, cursorColumn);
                OperationWriter?.Invoke(new ZMachineScreenOperation(
                    ZMachineScreenOperationKind.MoveCursor,
                    Window: SelectedWindow,
                    Row: cursorRow,
                    Column: cursorColumn));
                if (charactersConsumed < text.Length)
                {
                    _queuedText.Enqueue((text[charactersConsumed..], screenWriter));
                }

                return;
            }
        }

        screenWriter?.Invoke(outputChunk.ToString());
        if (IsWaitingForMore)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePrompt));
        }

        cursor.Set(cursorRow, cursorColumn);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.MoveCursor,
            Window: SelectedWindow,
            Row: cursorRow,
            Column: cursorColumn));
    }

    private void AdvanceLine(bool upperWindow, ref int row, ref int column)
    {
        column = 1;
        if (upperWindow)
        {
            if (row < UpperWindowHeight)
            {
                row++;
            }

            return;
        }

        var lowerBottom = LowerWindowBottom;
        if (Version == 4)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.ScrollWindow,
                Window: 0));
            row = lowerBottom;
        }
        else if (row < lowerBottom)
        {
            row++;
        }
        else
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.ScrollWindow,
                Window: 0));
        }

        _linesSinceMorePrompt++;
        var visibleLines = Version == 3 ? HeightInLines - 1 : HeightInLines;
        if (MorePromptsEnabled && visibleLines > 1 && HeightInLines != byte.MaxValue
            && _linesSinceMorePrompt >= visibleLines && !IsWaitingForMore)
        {
            IsWaitingForMore = true;
        }
    }

    private int MaximumUpperWindowHeight => Math.Max(0, HeightInLines - (Version == 3 ? 1 : 0));

    private int LowerWindowTop => Math.Min(HeightInLines, UpperWindowHeight + (Version == 3 ? 2 : 1));

    private int LowerWindowBottom => Math.Max(LowerWindowTop, HeightInLines);

    private void WriteDimensionsToHeader()
    {
        if (Version >= 4)
        {
            _memory.WriteByte(0x20, checked((byte)HeightInLines));
            _memory.WriteByte(0x21, checked((byte)WidthInCharacters));
        }

        if (Version >= 5)
        {
            _memory.WriteWord(0x22, WidthInUnits);
            _memory.WriteWord(0x24, HeightInUnits);
        }
    }

    private void NormalizeCursorsAfterResize()
    {
        var upper = _cursors[1];
        if (upper.Row > UpperWindowHeight || upper.Column > WidthInCharacters)
        {
            upper.Set(1, 1);
        }

        var lower = _cursors[0];
        if (Version == 4)
        {
            lower.Set(LowerWindowBottom, Math.Min(lower.Column, WidthInCharacters));
        }
        else if (lower.Row < LowerWindowTop || lower.Row > LowerWindowBottom || lower.Column > WidthInCharacters)
        {
            lower.Set(LowerWindowTop, 1);
        }
    }

    private void EnsureSplitWindowSupported()
    {
        if (Version < 3 || (Version == 3 && !SupportsUpperWindow))
        {
            throw new ZMachineExecutionException($"Split screen windows are not supported for version {Version}.");
        }
    }

    private void EnsureWindowExists(short window)
    {
        EnsureSplitWindowSupported();
        if (window is not 0 and not 1)
        {
            throw new ZMachineExecutionException($"Screen window {window} is not defined for version {Version}.");
        }
    }

    private CursorState GetCursorState(short window)
    {
        if (!_cursors.TryGetValue(window, out var cursor))
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        return cursor;
    }

    private byte ResolveColour(ushort requested, byte current, byte defaultColour)
    {
        if (requested == 0)
        {
            return current;
        }

        if (requested == 1)
        {
            return defaultColour;
        }

        if (requested is < 2 or > 9)
        {
            throw new ZMachineExecutionException($"Colour code {requested} is not defined for version 5.");
        }

        return checked((byte)requested);
    }

    private void SetFlag(byte mask, bool enabled)
    {
        var flags = _memory.ReadByte(Flags1Address);
        _memory.WriteByte(Flags1Address, enabled ? (byte)(flags | mask) : (byte)(flags & ~mask));
    }

    private sealed class CursorState(int row, int column)
    {
        public int Row { get; private set; } = row;

        public int Column { get; private set; } = column;

        public ZMachineCursorPosition Position => new(Row, Column);

        public void Set(int row, int column)
        {
            Row = row;
            Column = column;
        }
    }
}
