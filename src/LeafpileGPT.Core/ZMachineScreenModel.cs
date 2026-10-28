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
    SetFont,
    SetColours,
    MoveWindow,
    ResizeWindow,
    ScreenDimensions,
    SetWindowMargins,
    SetWindowStyle,
    SetTrueColours,
    SetCursorVisibility,
    SetWindowProperty,
    NewlineInterrupt,
    BufferScreen,
    FlushTextBuffer,
    FlushDisplay,
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
    string? Text = null,
    int SecondaryValue = 0,
    bool FixedPitch = false);

public sealed record ZMachineV6WindowSnapshot(
    short Window,
    ushort Y,
    ushort X,
    ushort Height,
    ushort Width,
    ushort CursorY,
    ushort CursorX,
    ushort LeftMargin,
    ushort RightMargin,
    ushort NewlineInterruptRoutine,
    ushort InterruptCountdown,
    ZMachineTextStyle TextStyle,
    ushort ColourData,
    ushort FontNumber,
    ushort FontSize,
    ushort Attributes,
    short LineCount,
    ushort TrueForegroundColour,
    ushort TrueBackgroundColour,
    bool CursorVisible);

/// <summary>Tracks V1–V6 screen state and exposes host-renderable screen operations.</summary>
public sealed class ZMachineScreenModel
{
    private const byte Flags1Address = 0x01;
    private const byte Flags2Address = 0x10;
    private const byte StatusUnavailableFlag = 0x10;
    private const byte UpperWindowAvailableFlag = 0x20;
    private const byte ColoursAvailableFlag = 0x01;
    private const ushort FixedPitchFlag = 0x0002;
    private const ushort CharacterGraphicsAvailableFlag = 0x0008;
    private readonly ZMachineMemory _memory;
    private readonly Queue<(string Text, Action<string>? Writer)> _queuedText = new();
    private readonly V6WindowState[] _v6Windows = Enumerable.Range(0, 8).Select(_ => new V6WindowState()).ToArray();
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
    private bool _v6SplitActive;
    private bool _screenUpdatesBuffered;
    private ZMachineTextStyle _textStyle;
    private ushort _fontNumber = ZMachineFontCatalog.NormalFont;
    private readonly byte _defaultFontWidth;
    private readonly byte _defaultFontHeight;
    private byte _foregroundColour;
    private byte _backgroundColour;

    public ZMachineScreenModel(ZMachineMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        var header = memory.Header;
        WidthInCharacters = header.ScreenWidthInCharacters is > 0 ? header.ScreenWidthInCharacters.Value : 80;
        HeightInLines = header.ScreenHeightInLines is > 0 ? header.ScreenHeightInLines.Value : 25;
        WidthInUnits = header.ScreenWidthInUnits is > 0 ? header.ScreenWidthInUnits.Value : (ushort)WidthInCharacters;
        HeightInUnits = header.ScreenHeightInUnits is > 0 ? header.ScreenHeightInUnits.Value : (ushort)HeightInLines;
        _defaultFontWidth = header.FontWidthInUnits is > 0 ? header.FontWidthInUnits.Value : (byte)1;
        _defaultFontHeight = header.FontHeightInUnits is > 0 ? header.FontHeightInUnits.Value : (byte)1;
        WriteDimensionsToHeader();

        if (Version == 3)
        {
            SetFlag(StatusUnavailableFlag, enabled: true);
            SetFlag(UpperWindowAvailableFlag, enabled: _supportsUpperWindow);
        }

        if (Version >= 5)
        {
            SetFlags2(CharacterGraphicsAvailableFlag, enabled: true);
            SetFlag(ColoursAvailableFlag, enabled: true);
            DefaultForegroundColour = 9;
            DefaultBackgroundColour = 2;
            ForegroundColour = DefaultForegroundColour;
            BackgroundColour = DefaultBackgroundColour;
            _memory.WriteByte(0x2C, DefaultBackgroundColour);
            _memory.WriteByte(0x2D, DefaultForegroundColour);
        }

        if (Version == 6)
        {
            InitializeV6Windows();
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

    public ushort CurrentFont => Version == 6 ? _v6Windows[SelectedWindow].FontNumber : _fontNumber;

    public bool RequiresFixedPitch => RequiresFixedPitchFor(SelectedWindow);

    public ZMachineTextStyle TextStyle
    {
        get => Version == 6 ? _v6Windows[SelectedWindow].TextStyle : _textStyle;
        private set => _textStyle = value;
    }

    public byte ForegroundColour
    {
        get => Version == 6 ? _v6Windows[SelectedWindow].Foreground : _foregroundColour;
        private set => _foregroundColour = value;
    }

    public byte BackgroundColour
    {
        get => Version == 6 ? _v6Windows[SelectedWindow].Background : _backgroundColour;
        private set => _backgroundColour = value;
    }

    public byte DefaultForegroundColour { get; private set; }

    public byte DefaultBackgroundColour { get; private set; }

    public bool IsBufferingEnabled => Version == 6
        ? (_v6Windows[SelectedWindow].Attributes & 0x08) != 0
        : Version <= 3 || (SelectedWindow == 0 && _lowerBufferingEnabled);

    public bool ScreenUpdatesBuffered => _screenUpdatesBuffered;

    public bool CopiesCurrentWindowToTranscript => Version != 6 || (_v6Windows[SelectedWindow].Attributes & 0x04) != 0;

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

    /// <summary>Synchronously invokes a game routine when a V6 newline countdown reaches zero.</summary>
    public Action<ushort>? NewlineInterruptHandler { get; set; }

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

    public ZMachineCursorPosition GetCursor(short window) => Version == 6
        ? new ZMachineCursorPosition(_v6Windows[ResolveWindowNumber(window)].CursorY, _v6Windows[ResolveWindowNumber(window)].CursorX)
        : GetCursorState(window).Position;

    public ZMachineCursorPosition CurrentCursor => GetCursor(SelectedWindow);

    public ZMachineV6WindowSnapshot GetV6Window(short window)
    {
        var state = GetV6WindowState(window);
        return state.Snapshot(ResolveWindowNumber(window));
    }

    public ushort GetWindowProperty(short window, ushort property)
    {
        var state = GetV6WindowState(window);
        return state.GetProperty(property);
    }

    public void PutWindowProperty(short window, ushort property, ushort value)
    {
        var state = GetV6WindowState(window);
        state.SetProperty(property, value);
        if (property == 15)
        {
            state.LineCountIsCustomized = true;
        }
        if (property is 2 or 3)
        {
            NormalizeV6Cursor(state);
        }
        else if (property is 6 or 7)
        {
            var leftEdge = (ushort)(state.LeftMargin + 1);
            var rightEdge = (ushort)Math.Max(leftEdge, state.Width - state.RightMargin + 1);
            if (state.CursorX < leftEdge || state.CursorX > rightEdge)
            {
                state.SetProperty(5, leftEdge);
            }
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetWindowProperty,
            Window: ResolveWindowNumber(window),
            Value: property,
            SecondaryValue: value));
    }

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
        if (Version == 6)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.ScreenDimensions,
                Row: HeightInUnits,
                Column: WidthInUnits));
        }
    }

    public void SplitWindow(ushort height)
    {
        if (Version == 6)
        {
            SplitV6Windows(height);
            return;
        }

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
        if (Version == 6)
        {
            FlushV6TextBuffer();
            SelectedWindow = ResolveWindowNumber(window);
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.SelectWindow,
                Window: SelectedWindow));
            return;
        }

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
        if (Version == 6)
        {
            SetCursorV6(row, column, SelectedWindow);
            return;
        }

        if (Version < 4 || SelectedWindow != 1)
        {
            return;
        }

        SetCursor(1, row, column);
    }

    public void SetCursor(short window, ushort row, ushort column)
    {
        if (Version == 6)
        {
            SetCursorV6(row, column, window);
            return;
        }

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

    public void SetV6Cursor(ushort row, ushort column, short window = -3)
    {
        SetCursorV6(row, column, window);
    }

    public void StoreCursorPosition(ushort arrayAddress)
    {
        if (Version < 4)
        {
            throw new ZMachineExecutionException("get_cursor is not available before version 4.");
        }

        _memory.ValidateWritableRange(arrayAddress, sizeof(ushort) * 2);
        if (Version == 6)
        {
            FlushV6TextBuffer();
        }

        var cursor = CurrentCursor;
        _memory.WriteWord(arrayAddress, checked((ushort)cursor.Row));
        _memory.WriteWord(checked((ushort)(arrayAddress + 2)), checked((ushort)cursor.Column));
    }

    public void EraseWindow(short window)
    {
        if (Version == 6)
        {
            EraseV6Window(window);
            return;
        }

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
        if (Version == 6)
        {
            EraseV6Line(value);
            return;
        }

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
        if (Version == 6)
        {
            var window = _v6Windows[SelectedWindow];
            window.SetProperty(14, enabled ? (ushort)(window.Attributes | 0x08) : (ushort)(window.Attributes & ~0x08));
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.SetWindowStyle,
                Window: SelectedWindow,
                Value: window.Attributes));
            return;
        }

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

        if (Version == 6)
        {
            if ((style & (ushort)ZMachineTextStyle.ReverseVideo) != 0
                && _v6Windows[SelectedWindow].TrueBackgroundColour == 0xFFFC)
            {
                throw new ZMachineExecutionException("Reverse video is not valid with a transparent V6 background.");
            }

            FlushV6TextBuffer();
            _v6Windows[SelectedWindow].SetProperty(10, (ushort)(style & 0x0F));
        }

        TextStyle = (ZMachineTextStyle)(style & 0x0F);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetTextStyle,
            Window: SelectedWindow,
            Value: (int)TextStyle,
            FixedPitch: RequiresFixedPitch));
    }

    /// <summary>Selects an available font and returns the previous font, or zero when unavailable.</summary>
    public ushort SetFont(ushort requestedFont, short? window = null)
    {
        if (Version < 5)
        {
            throw new ZMachineExecutionException("set_font is not available before version 5.");
        }

        if (Version != 6 && window is not null)
        {
            throw new ZMachineExecutionException("An explicit font window is only supported in version 6.");
        }

        var targetWindow = Version == 6 ? ResolveWindowNumber(window ?? -3) : SelectedWindow;
        var previousFont = Version == 6 ? _v6Windows[targetWindow].FontNumber : _fontNumber;
        if (requestedFont == 0)
        {
            return previousFont;
        }

        if (!ZMachineFontCatalog.IsAvailable(requestedFont))
        {
            return 0;
        }

        FlushV6TextBuffer(targetWindow);
        if (Version == 6)
        {
            var target = _v6Windows[targetWindow];
            target.SetProperty(12, requestedFont);
            var width = requestedFont == ZMachineFontCatalog.CharacterGraphicsFont ? (byte)8 : _defaultFontWidth;
            var height = requestedFont == ZMachineFontCatalog.CharacterGraphicsFont ? (byte)8 : _defaultFontHeight;
            target.SetProperty(13, (ushort)((height << 8) | width));
            if (!target.LineCountIsCustomized)
            {
                target.SetProperty(15, checked((ushort)Math.Max(0, target.Height / Math.Max(1, (int)height))));
            }
        }
        else
        {
            _fontNumber = requestedFont;
            var width = requestedFont == ZMachineFontCatalog.CharacterGraphicsFont ? (byte)1 : _defaultFontWidth;
            var height = requestedFont == ZMachineFontCatalog.CharacterGraphicsFont ? (byte)1 : _defaultFontHeight;
            _memory.WriteByte(0x26, width);
            _memory.WriteByte(0x27, height);
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetFont,
            Window: targetWindow,
            Value: requestedFont,
            SecondaryValue: previousFont,
            FixedPitch: RequiresFixedPitchFor(targetWindow)));
        return previousFont;
    }

    public void SetColours(ushort foreground, ushort background)
    {
        if (Version == 6)
        {
            SetV6Colours(foreground, background, SelectedWindow);
            return;
        }

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

    public void SetColours(ushort foreground, ushort background, short window)
    {
        if (Version == 6)
        {
            SetV6Colours(foreground, background, window);
            return;
        }

        if (window != SelectedWindow)
        {
            throw new ZMachineExecutionException("An explicit color window is only supported in version 6.");
        }

        SetColours(foreground, background);
    }

    public void SetTrueColours(ushort foreground, ushort background, short? window = null)
    {
        if (Version < 5)
        {
            throw new ZMachineExecutionException("set_true_colour is not available before version 5.");
        }

        var selected = Version == 6 ? ResolveWindowNumber(window ?? -3) : (short)0;
        if (Version == 5 && window is not null)
        {
            throw new ZMachineExecutionException("An explicit true-color window is only supported in version 6.");
        }

        if (Version == 5)
        {
            var v5Foreground = ResolveTrueColour(foreground, TrueColourForCode(ForegroundColour), TrueColourForCode(DefaultForegroundColour));
            var v5Background = ResolveTrueColour(background, TrueColourForCode(BackgroundColour), TrueColourForCode(DefaultBackgroundColour));
            if (v5Foreground >= 0xFFFC || v5Background >= 0xFFFC)
            {
                throw new ZMachineExecutionException("V5 does not support sampled or transparent true colors.");
            }

            SetColours(ClosestStandardColour(v5Foreground), ClosestStandardColour(v5Background));
            return;
        }

        var current = Version == 6 ? _v6Windows[selected] : null;
        var oldForeground = current?.TrueForegroundColour ?? TrueColourForCode(ForegroundColour);
        var oldBackground = current?.TrueBackgroundColour ?? TrueColourForCode(BackgroundColour);
        var trueForeground = ResolveTrueColour(foreground, oldForeground, TrueColourForCode(DefaultForegroundColour));
        var trueBackground = ResolveTrueColour(background, oldBackground, TrueColourForCode(DefaultBackgroundColour));
        if (Version == 6 && trueForeground == 0xFFFC)
        {
            throw new ZMachineExecutionException("Transparent true color is only valid for the V6 background.");
        }

        if (Version == 6 && current is not null && trueBackground == 0xFFFC && (current.Attributes & 0x02) != 0)
        {
            throw new ZMachineExecutionException("V6 windows with a transparent background cannot scroll.");
        }
        if (current is not null)
        {
            FlushV6TextBuffer(selected);
            current.SetReadOnlyProperty(16, trueForeground);
            current.SetReadOnlyProperty(17, trueBackground);
            current.SetProperty(11, PackColours(ColourCodeForTrueColour(trueForeground), ColourCodeForTrueColour(trueBackground)));
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetTrueColours,
            Window: selected,
            Value: trueForeground,
            SecondaryValue: trueBackground));
    }

    public void SetMargins(ushort left, ushort right, short window)
    {
        var state = GetV6WindowState(window);
        if ((uint)left + right > state.Width)
        {
            throw new ZMachineExecutionException(
                $"Window margins {left}+{right} exceed window width {state.Width}.");
        }

        state.SetProperty(6, left);
        state.SetProperty(7, right);
        var cursor = state.CursorX;
        var leftEdge = (ushort)(left + 1);
        var rightEdge = (ushort)Math.Max(leftEdge, state.Width - right + 1);
        if (cursor < leftEdge || cursor > rightEdge)
        {
            state.SetProperty(5, leftEdge);
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetWindowMargins,
            Window: ResolveWindowNumber(window),
            Row: left,
            Column: right));
    }

    public void MoveWindow(short window, ushort y, ushort x)
    {
        if (y == 0 || x == 0)
        {
            throw new ZMachineExecutionException("V6 window coordinates start at (1,1).");
        }

        var state = GetV6WindowState(window);
        state.SetProperty(0, y);
        state.SetProperty(1, x);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.MoveWindow,
            Window: ResolveWindowNumber(window),
            Row: y,
            Column: x));
    }

    public void ResizeWindow(short window, ushort height, ushort width)
    {
        var state = GetV6WindowState(window);
        state.SetProperty(2, height);
        state.SetProperty(3, width);
        if (state.CursorY > height + 1 || state.CursorX > width + 1)
        {
            state.SetProperty(4, 1);
            state.SetProperty(5, checked((ushort)(state.LeftMargin + 1)));
        }

        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.ResizeWindow,
            Window: ResolveWindowNumber(window),
            Row: height,
            Column: width));
    }

    public void ScrollWindow(short window, short pixels)
    {
        var state = GetV6WindowState(window);
        if (state.TrueBackgroundColour == 0xFFFC && pixels != 0)
        {
            throw new ZMachineExecutionException("V6 windows with a transparent background cannot scroll.");
        }
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.ScrollWindow,
            Window: ResolveWindowNumber(window),
            Value: pixels));
    }

    public void ChangeWindowStyle(short window, ushort flags, ushort operation)
    {
        var state = GetV6WindowState(window);
        if ((flags & ~0x0F) != 0 || operation > 3)
        {
            throw new ZMachineExecutionException($"window_style flags {flags} or operation {operation} are not valid.");
        }

        var attributes = operation switch
        {
            0 => flags,
            1 => (ushort)(state.Attributes | flags),
            2 => (ushort)(state.Attributes & ~flags),
            3 => (ushort)(state.Attributes ^ flags),
            _ => throw new InvalidOperationException()
        };
        if ((attributes & 0x02) != 0 && state.TrueBackgroundColour == 0xFFFC)
        {
            throw new ZMachineExecutionException("V6 windows with a transparent background cannot enable scrolling.");
        }

        state.SetProperty(14, attributes);
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetWindowStyle,
            Window: ResolveWindowNumber(window),
            Value: attributes));
    }

    public ushort BufferScreen(ushort mode)
    {
        if (Version != 6)
        {
            throw new ZMachineExecutionException("buffer_screen is only available in version 6.");
        }

        var oldMode = _screenUpdatesBuffered ? (ushort)1 : (ushort)0;
        if (mode == ushort.MaxValue)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.FlushDisplay));
            return oldMode;
        }

        if (mode > 1)
        {
            throw new ZMachineExecutionException($"buffer_screen mode {mode} must be 0, 1, or -1.");
        }

        _screenUpdatesBuffered = mode == 1;
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.BufferScreen,
            Value: mode));
        return oldMode;
    }

    public void FlushBeforeInput()
    {
        if (Version == 6)
        {
            FlushV6TextBuffer();
            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.FlushDisplay));
        }
    }

    private void InitializeV6Windows()
    {
        for (short index = 0; index < _v6Windows.Length; index++)
        {
            var window = _v6Windows[index];
            window.SetProperty(0, 1);
            window.SetProperty(1, 1);
            window.SetProperty(2, index == 0 ? HeightInUnits : (ushort)0);
            window.SetProperty(3, index is 0 or 1 ? WidthInUnits : (ushort)0);
            window.SetProperty(4, 1);
            window.SetProperty(5, 1);
            window.SetProperty(10, 0);
            window.SetProperty(11, PackColours(9, 2));
            window.SetProperty(12, 1);
            var fontWidth = _memory.Header.FontWidthInUnits.GetValueOrDefault(1);
            var fontHeight = _memory.Header.FontHeightInUnits.GetValueOrDefault(1);
            window.SetProperty(13, (ushort)((fontHeight << 8) | fontWidth));
            window.SetProperty(14, index == 0 ? (ushort)0x0E : (ushort)0x08);
            window.SetProperty(15, index == 0 ? checked((ushort)Math.Max(0, HeightInUnits / Math.Max(1, (int)fontHeight))) : (ushort)0);
            window.SetReadOnlyProperty(16, 0x7FFF);
            window.SetReadOnlyProperty(17, 0);
        }

        SetFlag(ColoursAvailableFlag, enabled: true);
        _memory.WriteByte(0x2C, DefaultBackgroundColour);
        _memory.WriteByte(0x2D, DefaultForegroundColour);
    }

    private void ResizeV6ScreenWindows()
    {
        if (Version != 6)
        {
            return;
        }

        var mainWindow = _v6Windows[0];
        mainWindow.SetProperty(3, WidthInUnits);
        if (_v6SplitActive)
        {
            var statusWindowHeight = Math.Min(_v6Windows[1].Height, HeightInUnits);
            _v6Windows[1].SetProperty(2, statusWindowHeight);
            _v6Windows[1].SetProperty(3, WidthInUnits);
            mainWindow.SetProperty(0, statusWindowHeight == ushort.MaxValue
                ? statusWindowHeight
                : checked((ushort)(statusWindowHeight + 1)));
            mainWindow.SetProperty(2, checked((ushort)(HeightInUnits - statusWindowHeight)));
        }
        else
        {
            mainWindow.SetProperty(2, HeightInUnits);
            _v6Windows[1].SetProperty(3, WidthInUnits);
        }

        if (!mainWindow.LineCountIsCustomized)
        {
            mainWindow.SetProperty(15, checked((ushort)Math.Max(1, mainWindow.Height / mainWindow.FontHeight)));
        }
        NormalizeV6Cursor(mainWindow);
        NormalizeV6Cursor(_v6Windows[1]);
    }

    private static void NormalizeV6Cursor(V6WindowState window)
    {
        if (window.CursorY > window.Height + 1 || window.CursorX > window.Width + 1)
        {
            window.SetProperty(4, 1);
            window.SetProperty(5, checked((ushort)(window.LeftMargin + 1)));
        }
    }

    private void SplitV6Windows(ushort height)
    {
        if (height > HeightInUnits)
        {
            throw new ZMachineExecutionException($"Window 1 height {height} exceeds screen height {HeightInUnits}.");
        }

        var top = _v6Windows[1];
        top.SetProperty(0, 1);
        top.SetProperty(1, 1);
        top.SetProperty(2, height);
        top.SetProperty(3, WidthInUnits);
        var lower = _v6Windows[0];
        lower.SetProperty(0, height == ushort.MaxValue ? height : checked((ushort)(height + 1)));
        lower.SetProperty(1, 1);
        lower.SetProperty(2, checked((ushort)(HeightInUnits - height)));
        lower.SetProperty(3, WidthInUnits);
        NormalizeV6Cursor(top);
        NormalizeV6Cursor(lower);
        _v6SplitActive = height != 0;
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SplitWindow,
            Window: 1,
            Value: height,
            SecondaryValue: HeightInUnits - height));
    }

    private void SetCursorV6(ushort y, ushort x, short windowNumber)
    {
        var resolved = ResolveWindowNumber(windowNumber);
        var window = _v6Windows[resolved];
        if (y == ushort.MaxValue)
        {
            window.CursorVisible = false;
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.SetCursorVisibility,
                Window: resolved,
                Value: 0));
            return;
        }

        if (y == ushort.MaxValue - 1)
        {
            window.CursorVisible = true;
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.SetCursorVisibility,
                Window: resolved,
                Value: 1));
            return;
        }

        var maximumY = (uint)window.Height + 1;
        var maximumX = (uint)window.Width + 1;
        if (y < 1 || x < 1 || y > maximumY || x > maximumX)
        {
            throw new ZMachineExecutionException($"Cursor position ({y},{x}) lies outside V6 window {resolved}.");
        }

        var leftEdge = checked((ushort)(window.LeftMargin + 1));
        var rightEdge = checked((ushort)Math.Max(leftEdge, window.Width - window.RightMargin + 1));
        if (x < leftEdge || x > rightEdge)
        {
            x = leftEdge;
        }

        window.SetProperty(4, checked((ushort)y));
        window.SetProperty(5, checked((ushort)x));
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.MoveCursor,
            Window: resolved,
            Row: y,
            Column: x));
    }

    private void EraseV6Window(short windowNumber)
    {
        if (windowNumber == -1)
        {
            var canClear = _v6Windows[0].TrueBackgroundColour != 0xFFFC;
            var oldHeight = _v6Windows[1].Height;
            if (_v6SplitActive)
            {
                SplitV6Windows(0);
            }

            SelectedWindow = 0;
            for (short index = 0; index < 2; index++)
            {
                _v6Windows[index].SetProperty(4, 1);
                _v6Windows[index].SetProperty(5, 1);
            }

            if (canClear)
            {
                OperationWriter?.Invoke(new ZMachineScreenOperation(
                    ZMachineScreenOperationKind.ClearScreen,
                    Value: 0,
                    SecondaryValue: oldHeight));
            }

            return;
        }

        if (windowNumber == -2)
        {
            if (_v6Windows[SelectedWindow].TrueBackgroundColour == 0xFFFC)
            {
                return;
            }

            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearScreen, Value: -2));
            return;
        }

        var resolved = ResolveWindowNumber(windowNumber);
        var window = _v6Windows[resolved];
        if (window.TrueBackgroundColour == 0xFFFC)
        {
            return;
        }

        window.SetProperty(4, 1);
        window.SetProperty(5, 1);
        OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.ClearWindow, Window: resolved));
    }

    private void EraseV6Line(ushort value)
    {
        if (value == 0)
        {
            return;
        }

        var window = _v6Windows[SelectedWindow];
        if (window.TrueBackgroundColour == 0xFFFC)
        {
            return;
        }
        var length = value == 1 ? int.MaxValue : value - 1;
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.EraseLine,
            Window: SelectedWindow,
            Row: window.CursorY,
            Column: window.CursorX,
            Value: length));
    }

    private void SetV6Colours(ushort foreground, ushort background, short windowNumber)
    {
        var resolved = ResolveWindowNumber(windowNumber);
        var window = _v6Windows[resolved];
        var currentForeground = (byte)(window.ColourData & 0xFF);
        var currentBackground = (byte)(window.ColourData >> 8);
        var nextForeground = ResolveV6Colour(foreground, currentForeground, DefaultForegroundColour);
        var nextBackground = ResolveV6Colour(background, currentBackground, DefaultBackgroundColour);
        if (nextForeground == 15)
        {
            throw new ZMachineExecutionException("Transparent color 15 is only valid for the V6 background.");
        }

        if (nextBackground == 15 && (window.Attributes & 0x02) != 0)
        {
            throw new ZMachineExecutionException("V6 windows with a transparent background cannot scroll.");
        }

        FlushV6TextBuffer(resolved);
        window.SetProperty(11, PackColours(nextForeground, nextBackground));
        window.SetReadOnlyProperty(16, TrueColourForCode(nextForeground));
        window.SetReadOnlyProperty(17, TrueColourForCode(nextBackground));
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.SetColours,
            Window: resolved,
            Value: window.ColourData));
    }

    private V6WindowState GetV6WindowState(short window)
    {
        if (Version != 6)
        {
            throw new ZMachineExecutionException("V6 window properties are only available in version 6.");
        }

        return _v6Windows[ResolveWindowNumber(window)];
    }

    private short ResolveWindowNumber(short window)
    {
        if (Version != 6)
        {
            throw new ZMachineExecutionException("Eight-window addressing is only available in version 6.");
        }

        var resolved = window == -3 ? SelectedWindow : window;
        if (resolved is < 0 or > 7)
        {
            throw new ZMachineExecutionException($"V6 window number {window} is not defined.");
        }

        return resolved;
    }

    private static ushort ResolveTrueColour(ushort requested, ushort current, ushort defaultColour)
    {
        if (requested == ushort.MaxValue)
        {
            return defaultColour;
        }

        if (requested == ushort.MaxValue - 1)
        {
            return current;
        }

        if (requested == ushort.MaxValue - 2 || requested == ushort.MaxValue - 3)
        {
            return requested;
        }

        if ((requested & 0x8000) != 0)
        {
            throw new ZMachineExecutionException($"15-bit true color value 0x{requested:X4} has its high bit set.");
        }

        return requested;
    }

    private static byte ResolveV6Colour(ushort requested, byte current, byte defaultColour)
    {
        if (requested == 0)
        {
            return current;
        }

        if (requested == 1)
        {
            return defaultColour;
        }

        if (requested == ushort.MaxValue)
        {
            return 16;
        }

        if ((requested < 2 || requested > 12) && requested != 15)
        {
            throw new ZMachineExecutionException($"Color code {requested} is not defined for version 6.");
        }

        return checked((byte)requested);
    }

    private static ushort PackColours(byte foreground, byte background) => (ushort)((background << 8) | foreground);

    private static ushort TrueColourForCode(byte code) => code switch
    {
        2 => 0x0000,
        3 => 0x001D,
        4 => 0x0340,
        5 => 0x03BD,
        6 => 0x59A0,
        7 => 0x7C1F,
        8 => 0x77A0,
        9 => 0x7FFF,
        10 => 0x5AD6,
        11 => 0x4631,
        12 => 0x2D6B,
        15 => 0xFFFC,
        _ => 0x7FFF
    };

    private static byte ColourCodeForTrueColour(ushort colour)
    {
        for (byte code = 2; code <= 12; code++)
        {
            if (TrueColourForCode(code) == colour)
            {
                return code;
            }
        }

        return colour == 0xFFFC ? (byte)15 : (byte)16;
    }

    private static byte ClosestStandardColour(ushort colour)
    {
        var red = colour & 0x1F;
        var green = (colour >> 5) & 0x1F;
        var blue = (colour >> 10) & 0x1F;
        var closest = (byte)2;
        var minimumDistance = int.MaxValue;
        for (byte code = 2; code <= 9; code++)
        {
            var candidate = TrueColourForCode(code);
            var candidateRed = candidate & 0x1F;
            var candidateGreen = (candidate >> 5) & 0x1F;
            var candidateBlue = (candidate >> 10) & 0x1F;
            var distance = Math.Abs(red - candidateRed) + Math.Abs(green - candidateGreen) + Math.Abs(blue - candidateBlue);
            if (distance < minimumDistance)
            {
                minimumDistance = distance;
                closest = code;
            }
        }

        return closest;
    }

    private sealed class V6WindowState
    {
        private readonly ushort[] _properties = new ushort[18];

        public ushort Y => _properties[0];

        public ushort X => _properties[1];

        public ushort Height => _properties[2];

        public ushort Width => _properties[3];

        public ushort CursorY => _properties[4];

        public ushort CursorX => _properties[5];

        public ushort LeftMargin => _properties[6];

        public ushort RightMargin => _properties[7];

        public ushort NewlineInterruptRoutine => _properties[8];

        public ushort InterruptCountdown => _properties[9];

        public ZMachineTextStyle TextStyle => (ZMachineTextStyle)(_properties[10] & 0x0F);

        public ushort ColourData => _properties[11];

        public byte Foreground => (byte)(_properties[11] & 0xFF);

        public byte Background => (byte)(_properties[11] >> 8);

        public ushort FontNumber => _properties[12];

        public ushort FontSize => _properties[13];

        public ushort Attributes => _properties[14];

        public short LineCount => unchecked((short)_properties[15]);

        public ushort TrueForegroundColour => _properties[16];

        public ushort TrueBackgroundColour => _properties[17];

        public ushort FontWidth => (byte)FontSize is 0 ? (ushort)1 : (byte)FontSize;

        public ushort FontHeight => (byte)(FontSize >> 8) is 0 ? (ushort)1 : (byte)(FontSize >> 8);

        public bool CursorVisible { get; set; } = true;

        public bool LineCountIsCustomized { get; set; }

        public ushort GetProperty(ushort property)
        {
            if (property >= _properties.Length)
            {
                throw new ZMachineExecutionException($"V6 window property {property} is not defined.");
            }

            return _properties[property];
        }

        public void SetProperty(ushort property, ushort value)
        {
            if (property >= _properties.Length)
            {
                throw new ZMachineExecutionException($"V6 window property {property} is not defined.");
            }

            if (property >= 16)
            {
                throw new ZMachineExecutionException($"V6 window property {property} is read-only.");
            }

            _properties[property] = value;
        }

        public void SetReadOnlyProperty(ushort property, ushort value) => _properties[property] = value;

        public ZMachineV6WindowSnapshot Snapshot(short window) => new(
            window,
            Y,
            X,
            Height,
            Width,
            CursorY,
            CursorX,
            LeftMargin,
            RightMargin,
            NewlineInterruptRoutine,
            InterruptCountdown,
            TextStyle,
            ColourData,
            FontNumber,
            FontSize,
            Attributes,
            LineCount,
            TrueForegroundColour,
            TrueBackgroundColour,
            CursorVisible);
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
        if (Version == 6)
        {
            var window = _v6Windows[SelectedWindow];
            window.SetProperty(15, checked((ushort)Math.Max(1, window.Height / window.FontHeight)));
        }
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
        if (Version == 6)
        {
            WriteV6TextImmediately(text, screenWriter);
            return;
        }

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

    private void WriteV6TextImmediately(string text, Action<string>? screenWriter)
    {
        var windowNumber = SelectedWindow;
        var window = _v6Windows[windowNumber];
        if (window.Height == 0 || window.Width == 0)
        {
            return;
        }

        var cursorY = (int)window.CursorY;
        var cursorX = (int)window.CursorX;
        var leftEdge = window.LeftMargin + 1;
        var rightEdge = Math.Max(leftEdge, window.Width - window.RightMargin);
        var wrap = (window.Attributes & 0x01) != 0;
        var outputChunk = new StringBuilder();
        var charactersConsumed = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeLength = rune.Utf16SequenceLength;
            if (rune.Value is '\r' or '\n')
            {
                outputChunk.Append(rune.ToString());
                charactersConsumed += runeLength;
                EmitV6TextChunk(outputChunk, screenWriter);
                AdvanceV6Line(windowNumber, window, ref cursorY, ref cursorX);
                StoreV6Cursor(windowNumber, window, cursorY, cursorX);
                if (IsWaitingForMore)
                {
                    OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePrompt));
                    QueueV6Remainder(text, charactersConsumed, screenWriter);
                    return;
                }
            }
            else
            {
                if (wrap && (rune.Value is ' ' or '\t') && (window.Attributes & 0x08) != 0)
                {
                    var nextWordWidth = GetNextWordWidth(text, charactersConsumed + runeLength, window.FontWidth);
                    var availableWidth = Math.Max(0, rightEdge - leftEdge + window.FontWidth);
                    if (nextWordWidth <= availableWidth && cursorX > leftEdge
                        && cursorX + window.FontWidth + nextWordWidth - 1 > rightEdge)
                    {
                        EmitV6TextChunk(outputChunk, screenWriter);
                        AdvanceV6Line(windowNumber, window, ref cursorY, ref cursorX);
                        charactersConsumed += runeLength;
                        StoreV6Cursor(windowNumber, window, cursorY, cursorX);
                        if (IsWaitingForMore)
                        {
                            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePrompt));
                            QueueV6Remainder(text, charactersConsumed, screenWriter);
                            return;
                        }

                        continue;
                    }
                }

                if (cursorX + window.FontWidth - 1 > rightEdge)
                {
                    if (wrap)
                    {
                        EmitV6TextChunk(outputChunk, screenWriter);
                        AdvanceV6Line(windowNumber, window, ref cursorY, ref cursorX);
                        StoreV6Cursor(windowNumber, window, cursorY, cursorX);
                        if (IsWaitingForMore)
                        {
                            OperationWriter?.Invoke(new ZMachineScreenOperation(ZMachineScreenOperationKind.MorePrompt));
                            QueueV6Remainder(text, charactersConsumed, screenWriter);
                            return;
                        }
                    }
                    else
                    {
                        cursorX = rightEdge;
                        charactersConsumed += runeLength;
                        continue;
                    }
                }

                outputChunk.Append(rune.ToString());
                cursorX += window.FontWidth;
            }

            charactersConsumed += runeLength;
        }

        EmitV6TextChunk(outputChunk, screenWriter);
        StoreV6Cursor(windowNumber, window, cursorY, cursorX);
    }

    private static void EmitV6TextChunk(StringBuilder output, Action<string>? screenWriter)
    {
        if (output.Length == 0)
        {
            return;
        }

        screenWriter?.Invoke(output.ToString());
        output.Clear();
    }

    private void StoreV6Cursor(short windowNumber, V6WindowState window, int cursorY, int cursorX)
    {
        window.SetProperty(4, checked((ushort)cursorY));
        window.SetProperty(5, checked((ushort)cursorX));
        OperationWriter?.Invoke(new ZMachineScreenOperation(
            ZMachineScreenOperationKind.MoveCursor,
            Window: windowNumber,
            Row: cursorY,
            Column: cursorX));
    }

    private void QueueV6Remainder(string text, int consumed, Action<string>? screenWriter)
    {
        if (consumed < text.Length)
        {
            _queuedText.Enqueue((text[consumed..], screenWriter));
        }
    }

    private void AdvanceV6Line(short windowNumber, V6WindowState window, ref int cursorY, ref int cursorX)
    {
        cursorX = window.LeftMargin + 1;
        var fontHeight = window.FontHeight;
        var bottom = window.Height;
        var nextY = cursorY + fontHeight;
        if (window.Height > 0 && nextY + fontHeight - 1 > bottom)
        {
            if ((window.Attributes & 0x02) != 0)
            {
                OperationWriter?.Invoke(new ZMachineScreenOperation(
                    ZMachineScreenOperationKind.ScrollWindow,
                    Window: windowNumber,
                    Value: fontHeight));
                nextY = Math.Max(1, bottom - fontHeight + 1);
            }
            else
            {
                nextY = Math.Max(1, bottom - fontHeight + 1);
            }
        }

        cursorY = nextY;
        if (window.LineCount > -999)
        {
            var nextLineCount = Math.Max(-999, window.LineCount - 1);
            window.SetProperty(15, unchecked((ushort)(short)nextLineCount));
            if (MorePromptsEnabled && nextLineCount == 0)
            {
                IsWaitingForMore = true;
            }
        }

        if (window.InterruptCountdown > 0)
        {
            var countdown = checked((ushort)(window.InterruptCountdown - 1));
            window.SetProperty(9, countdown);
            if (countdown == 0 && window.NewlineInterruptRoutine != 0)
            {
                OperationWriter?.Invoke(new ZMachineScreenOperation(
                    ZMachineScreenOperationKind.NewlineInterrupt,
                    Window: windowNumber,
                    Value: window.NewlineInterruptRoutine));
                NewlineInterruptHandler?.Invoke(window.NewlineInterruptRoutine);
            }
        }
    }

    private static int GetNextWordWidth(string text, int start, ushort fontWidth)
    {
        var width = 0;
        foreach (var rune in text.AsSpan(start).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                break;
            }

            width += fontWidth;
        }

        return width;
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

        if (Version == 6)
        {
            ResizeV6ScreenWindows();
        }
    }

    private void FlushV6TextBuffer(short? windowNumber = null)
    {
        var resolved = windowNumber ?? SelectedWindow;
        if (Version == 6 && (_v6Windows[resolved].Attributes & 0x08) != 0)
        {
            OperationWriter?.Invoke(new ZMachineScreenOperation(
                ZMachineScreenOperationKind.FlushTextBuffer,
                Window: resolved));
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

    private void SetFlags2(ushort mask, bool enabled)
    {
        var flags = _memory.ReadWord(Flags2Address);
        _memory.WriteWord(Flags2Address, enabled ? (ushort)(flags | mask) : (ushort)(flags & ~mask));
    }

    private bool RequiresFixedPitchFor(short window)
    {
        var font = Version == 6 ? _v6Windows[window].FontNumber : _fontNumber;
        var style = Version == 6 ? _v6Windows[window].TextStyle : _textStyle;
        return (_memory.ReadWord(Flags2Address) & FixedPitchFlag) != 0
            || (style & ZMachineTextStyle.FixedPitch) != 0
            || font is ZMachineFontCatalog.CharacterGraphicsFont or ZMachineFontCatalog.CourierFont;
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
