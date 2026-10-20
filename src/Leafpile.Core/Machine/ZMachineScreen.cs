using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Tracks the text screen and versioned window state exposed by the Z-machine.</summary>
public sealed class ZMachineScreen
{
    private sealed class WindowState
    {
        public int Y { get; set; } = 1;
        public int X { get; set; } = 1;
        public int Height { get; set; }
        public int Width { get; set; }
        public int CursorY { get; set; } = 1;
        public int CursorX { get; set; } = 1;
        public int Attributes { get; set; }
        public int Style { get; set; }
        public int Foreground { get; set; } = 2;
        public int Background { get; set; } = 9;
        public int TrueForeground { get; set; }
        public int TrueBackground { get; set; } = 0x7fff;
        public int Font { get; set; } = 1;
        public int FontWidth { get; set; } = 8;
        public int FontHeight { get; set; } = 16;
        public int LeftMargin { get; set; }
        public int RightMargin { get; set; }
        public int LineCount { get; set; }
        public int InterruptRoutine { get; set; }
        public int InterruptCountdown { get; set; }
    }

    private readonly ZMachineMemory _memory;
    private readonly WindowState[] _v6Windows = Enumerable.Range(0, 8).Select(_ => new WindowState()).ToArray();
    private char[][] _lowerCells;
    private char[][] _upperCells;
    private int _upperCursorRow;
    private int _upperCursorColumn;
    private int _lowerCursorRow;
    private int _lowerCursorColumn;
    private int _currentFont = 1;

    internal ZMachineScreen(ZMachineMemory memory, StoryHeader header)
    {
        _memory = memory;
        Version = header.Version;
        WidthCharacters = 80;
        HeightLines = 25;
        WidthUnits = header.Version >= 5 ? 640 : WidthCharacters;
        HeightUnits = header.Version >= 5 ? 400 : HeightLines;
        _lowerCells = CreateGrid(HeightLines, WidthCharacters);
        _upperCells = CreateGrid(HeightLines, WidthCharacters);
        UpperWindowHeight = 0;
        SelectedWindow = 0;
        Foreground = 2;
        Background = 9;
        LowerBufferingEnabled = true;
        _v6Windows[0].Width = WidthUnits;
        _v6Windows[0].Height = HeightUnits;
        _v6Windows[0].Attributes = 1 | 4 | 8;
        _v6Windows[1].Width = WidthUnits;
        _v6Windows[1].CursorX = WidthUnits + 1;
        _v6Windows[0].Background = 9;
        _v6Windows[0].Foreground = 2;
        for (var index = 1; index < _v6Windows.Length; index++)
        {
            _v6Windows[index].Attributes = 8;
        }
        TextStyle = (_memory.ReadWord(0x10) & 0x02) != 0 ? 8 : 0;
        if (Version != 5)
        {
            _lowerCursorRow = HeightLines - 1;
        }

        ConfigureHeader();
    }

    public byte Version { get; }

    public int WidthCharacters { get; private set; }

    public int HeightLines { get; private set; }

    public int WidthUnits { get; private set; }

    public int HeightUnits { get; private set; }

    public int SelectedWindow { get; private set; }

    public int UpperWindowHeight { get; private set; }

    public int CursorRow => Version == 6 ? _v6Windows[SelectedWindow].CursorY - 1 : SelectedWindow == 1 ? _upperCursorRow : _lowerCursorRow;

    public int CursorColumn => Version == 6 ? _v6Windows[SelectedWindow].CursorX - 1 : SelectedWindow == 1 ? _upperCursorColumn : _lowerCursorColumn;

    public int TextStyle { get; private set; }

    public short Foreground { get; private set; }

    public short Background { get; private set; }

    public bool LowerBufferingEnabled { get; private set; }

    public bool CursorVisible { get; private set; } = true;

    public int BufferScreenMode { get; private set; }

    public int MouseWindow { get; private set; } = 1;

    public bool CopyTextToTranscript => Version != 6 || (_v6Windows[SelectedWindow].Attributes & 4) != 0;

    internal Action<ushort>? NewlineInterrupt { get; set; }

    public int GetFormattedStreamWidth(short width)
    {
        var window = width < 0 ? SelectedWindow : width;
        var pixels = width < 0
            ? -width
            : GetWindowProperty(checked((ushort)width), 3)
                - GetWindowProperty(checked((ushort)window), 6)
                - GetWindowProperty(checked((ushort)window), 7);
        var fontSize = GetWindowProperty(checked((ushort)window), 13);
        var characterWidth = Math.Max(1, fontSize & 0xff);
        return Math.Max(1, pixels / characterWidth);
    }

    public IReadOnlyList<int> V6WindowBounds(int window) => Version == 6 && window is >= 0 and < 8
        ? new[] { _v6Windows[window].Y, _v6Windows[window].X, _v6Windows[window].Height, _v6Windows[window].Width }
        : throw new ArgumentOutOfRangeException(nameof(window));

    public bool UsesFixedPitch => Version == 6
        ? _v6Windows[SelectedWindow].Font == 4 || (_v6Windows[SelectedWindow].Style & 0x08) != 0
        : (Version <= 5 || Version >= 7) && ((SelectedWindow == 1 && Version >= 4) || _currentFont == 4 || (TextStyle & 0x08) != 0 || (_memory.ReadWord(0x10) & 0x02) != 0);

    public string StatusLine { get; private set; } = string.Empty;

    public string LowerWindowText => Render(_lowerCells);

    public string UpperWindowText => Render(_upperCells, UpperWindowHeight);

    public void SetDimensions(int columns, int rows, int widthUnits, int heightUnits)
    {
        if (columns is < 1 or > byte.MaxValue || rows is < 1 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Screen rows and columns must be between 1 and 255.");
        }

        if (widthUnits is < 1 or > ushort.MaxValue || heightUnits is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(widthUnits), "Screen dimensions in units must be between 1 and 65535.");
        }

        if (Version <= 4 && (widthUnits != columns || heightUnits != rows))
        {
            throw new ArgumentException("Versions 1 through 4 measure screen units in characters.");
        }

        var previousWidthUnits = WidthUnits;
        var previousHeightUnits = HeightUnits;
        WidthCharacters = columns;
        HeightLines = rows;
        WidthUnits = widthUnits;
        HeightUnits = heightUnits;
        if (Version == 6)
        {
            if (_v6Windows[0].X == 1 && _v6Windows[0].Y == 1 && _v6Windows[0].Width == previousWidthUnits && _v6Windows[0].Height == previousHeightUnits)
            {
                ResizeWindow(0, checked((ushort)heightUnits), checked((ushort)widthUnits));
            }
            if (_v6Windows[1].X == 1 && _v6Windows[1].Width == previousWidthUnits)
            {
                ResizeWindow(1, checked((ushort)_v6Windows[1].Height), checked((ushort)widthUnits));
            }
        }
        UpperWindowHeight = Math.Min(UpperWindowHeight, rows);
        _lowerCells = ResizeGrid(_lowerCells, rows, columns);
        _upperCells = ResizeGrid(_upperCells, rows, columns);
        _upperCursorRow = Math.Min(_upperCursorRow, Math.Max(0, UpperWindowHeight - 1));
        _upperCursorColumn = Math.Min(_upperCursorColumn, columns - 1);
        _lowerCursorRow = Math.Min(_lowerCursorRow, rows - 1);
        _lowerCursorColumn = Math.Min(_lowerCursorColumn, columns - 1);
        if (Version >= 4)
        {
            _memory.WriteByte(0x20, checked((byte)rows));
            _memory.WriteByte(0x21, checked((byte)columns));
            if (Version >= 5)
            {
                _memory.WriteWord(0x22, checked((ushort)widthUnits));
                _memory.WriteWord(0x24, checked((ushort)heightUnits));
            }
        }
    }

    public void ShowStatus(string location, ushort second, ushort third)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (Version > 3)
        {
            return;
        }

        var isTimeGame = Version == 3 && (_memory.ReadByte(1) & 0x02) != 0;
        var right = isTimeGame
            ? $"{second:00}:{third:00}"
            : $"Score: {(short)second}  Moves: {third}";
        var available = Math.Max(0, WidthCharacters - right.Length - 2);
        if (location.Length > available)
        {
            var prefixLength = Math.Max(0, available - 3);
            var lastSpace = location.LastIndexOf(' ', Math.Min(prefixLength, location.Length - 1), Math.Min(prefixLength + 1, location.Length));
            location = lastSpace > 0 ? location[..lastSpace] + "..." : location[..prefixLength] + "...";
        }

        StatusLine = location.PadRight(available) + "  " + right;
    }

    public void SplitWindow(ushort lines)
    {
        if (Version == 6)
        {
            var height = Math.Min(lines, HeightUnits);
            ResizeWindow(1, checked((ushort)height), checked((ushort)WidthUnits));
            MoveWindow(1, 1, 1);
            ResizeWindow(0, checked((ushort)(HeightUnits - height)), checked((ushort)WidthUnits));
            MoveWindow(0, checked((ushort)(height + 1)), 1);
            return;
        }

        UpperWindowHeight = Math.Min(lines, HeightLines);
        if (Version == 3)
        {
            ClearGrid(_upperCells);
            _upperCursorRow = 0;
            _upperCursorColumn = 0;
        }

        if (Version == 4)
        {
            _lowerCursorRow = HeightLines - 1;
        }
        else if (Version >= 5 && _lowerCursorRow < UpperWindowHeight)
        {
            _lowerCursorRow = UpperWindowHeight;
        }
    }

    public void SelectWindow(ushort window)
    {
        if (Version == 6)
        {
            if (window > 7) throw new ZMachineRuntimeException($"Window {window} is not defined in version 6.");
            SelectedWindow = window;
            return;
        }
        if (window > 1)
        {
            throw new ZMachineRuntimeException($"Text window {window} is not defined in version {Version}.");
        }

        SelectedWindow = window;
        if (window == 1)
        {
            _upperCursorRow = 0;
            _upperCursorColumn = 0;
        }
    }

    public void SetCursor(ushort row, ushort column)
    {
        if (Version == 6)
        {
            SetCursorV6(SelectedWindow, row, column);
            return;
        }
        if (Version < 4 || SelectedWindow != 1)
        {
            return;
        }

        if (row == 0 || column == 0 || row > UpperWindowHeight || column > WidthCharacters)
        {
            throw new ZMachineRuntimeException("set_cursor position is outside the current upper window.");
        }

        _upperCursorRow = row - 1;
        _upperCursorColumn = column - 1;
    }

    public void SetCursorV6(int window, ushort y, ushort x)
    {
        if (Version != 6) return;
        var target = ResolveWindow(window);
        if (y == ushort.MaxValue)
        {
            CursorVisible = false;
            return;
        }
        if (y == ushort.MaxValue - 1)
        {
            CursorVisible = true;
            return;
        }
        if (y < 1 || x < 1 || y > target.Height + 1 || x > target.Width + 1)
            throw new ZMachineRuntimeException("set_cursor position is outside the selected window.");
        target.CursorY = y;
        target.CursorX = x;
        if (window == -3 || window == SelectedWindow)
        {
            _upperCursorRow = y - 1;
            _upperCursorColumn = x - 1;
        }
    }

    public (ushort Y, ushort X) GetV6Cursor() => Version == 6
        ? (checked((ushort)_v6Windows[SelectedWindow].CursorY), checked((ushort)_v6Windows[SelectedWindow].CursorX))
        : (checked((ushort)(CursorRow + 1)), checked((ushort)(CursorColumn + 1)));

    public ushort GetWindowProperty(ushort window, ushort property)
    {
        var target = ResolveWindow(window);
        return property switch
        {
            0 => checked((ushort)target.Y),
            1 => checked((ushort)target.X),
            2 => checked((ushort)target.Height),
            3 => checked((ushort)target.Width),
            4 => checked((ushort)target.CursorY),
            5 => checked((ushort)target.CursorX),
            6 => checked((ushort)target.LeftMargin),
            7 => checked((ushort)target.RightMargin),
            8 => checked((ushort)target.InterruptRoutine),
            9 => checked((ushort)target.InterruptCountdown),
            10 => checked((ushort)target.Style),
            11 => unchecked((ushort)((target.Background << 8) | (target.Foreground & 0xff))),
            12 => checked((ushort)target.Font),
            13 => checked((ushort)((target.FontHeight << 8) | target.FontWidth)),
            14 => checked((ushort)target.Attributes),
            15 => unchecked((ushort)target.LineCount),
            16 => unchecked((ushort)target.TrueForeground),
            17 => unchecked((ushort)target.TrueBackground),
            _ => throw new ZMachineRuntimeException($"Window property {property} is not defined.")
        };
    }

    public void PutWindowProperty(ushort window, ushort property, ushort value)
    {
        var target = ResolveWindow(window);
        switch (property)
        {
            case 0: target.Y = value; break;
            case 1: target.X = value; break;
            case 2: ResizeWindow(checked((ushort)Array.IndexOf(_v6Windows, target)), value, checked((ushort)target.Width)); break;
            case 3: ResizeWindow(checked((ushort)Array.IndexOf(_v6Windows, target)), checked((ushort)target.Height), value); break;
            case 4: target.CursorY = value; break;
            case 5: target.CursorX = value; break;
            case 6: target.LeftMargin = value; break;
            case 7: target.RightMargin = value; break;
            case 8: target.InterruptRoutine = value; break;
            case 9: target.InterruptCountdown = value; break;
            case 10: target.Style = value; break;
            case 11:
                target.Foreground = value & 0xff;
                target.Background = value >> 8;
                target.TrueForeground = TrueColour(target.Foreground);
                target.TrueBackground = TrueColour(target.Background);
                break;
            case 12: target.Font = value; break;
            case 13: target.FontHeight = value >> 8; target.FontWidth = value & 0xff; break;
            case 14: target.Attributes = value & 0x0f; break;
            case 15: target.LineCount = unchecked((short)value); break;
            default: throw new ZMachineRuntimeException($"Window property {property} is read-only or controlled by a specialized opcode.");
        }
    }

    public void MoveWindow(ushort window, ushort y, ushort x)
    {
        var target = ResolveWindow(window); target.Y = y; target.X = x;
    }

    public void ResizeWindow(ushort window, ushort height, ushort width)
    {
        var target = ResolveWindow(window); target.Height = height; target.Width = width;
        if (target.CursorY > height + 1 || target.CursorX > width + 1) { target.CursorY = 1; target.CursorX = Math.Max(1, target.LeftMargin + 1); }
    }

    public void ScrollWindow(ushort window, ushort amount)
    {
        _ = ResolveWindow(window);
        _ = amount;
    }

    public void SetWindowStyle(ushort window, ushort flags, ushort operation)
    {
        var target = ResolveWindow(window); var mask = flags & 0x0f;
        target.Attributes = operation switch { 0 => mask, 1 => target.Attributes | mask, 2 => target.Attributes & ~mask, 3 => target.Attributes ^ mask, _ => target.Attributes };
    }

    public ushort SetFont(ushort font, ushort window)
    {
        if (Version != 6)
        {
            if (Version < 5 || window != 0xfffd) return 0;
            var previous = _currentFont;
            if (font == 0) return checked((ushort)previous);
            if (font is 1 or 4) _currentFont = font;
            return font is 1 or 4 ? checked((ushort)previous) : (ushort)0;
        }

        var target = ResolveWindow(window); var old = target.Font;
        if (font == 0) return checked((ushort)old);
        if (font is 1 or 3 or 4)
        {
            target.Font = font;
            target.FontWidth = 8;
            target.FontHeight = font == 3 ? 8 : 16;
        }
        return font is 1 or 3 or 4 ? checked((ushort)old) : (ushort)0;
    }

    public void SetV6Colour(ushort foreground, ushort background, ushort window, bool trueColour = false)
    {
        var target = ResolveWindow(window);
        if (trueColour)
        {
            SetTrueColour(target, foreground, foreground: true);
            SetTrueColour(target, background, foreground: false);
        }
        else
        {
            SetV6PaletteColour(target, foreground, foreground: true);
            SetV6PaletteColour(target, background, foreground: false);
        }
    }

    public void SetTrueColour(ushort foreground, ushort background, ushort window)
    {
        if (Version == 6)
        {
            SetV6Colour(foreground, background, window, trueColour: true);
            return;
        }

        if (Version < 5 || window != 0xfffd) return;
        Foreground = ResolveTrueColour(foreground, Foreground, _memory.ReadByte(0x2d));
        Background = ResolveTrueColour(background, Background, _memory.ReadByte(0x2c));
    }

    private static short ResolveTrueColour(ushort requested, short current, byte defaultColour)
    {
        var signed = unchecked((short)requested);
        if (signed == -2) return current;
        if (signed == -1) return defaultColour;
        return signed >= 0 && (requested & 0x8000) == 0 ? signed : current;
    }

    public ushort SetBufferScreen(ushort mode)
    {
        var previous = BufferScreenMode;
        if (mode <= 1) BufferScreenMode = mode;
        return checked((ushort)previous);
    }

    public void SetMargins(ushort left, ushort right, ushort window)
    {
        var target = ResolveWindow(window); target.LeftMargin = left; target.RightMargin = right;
        if (target.CursorX <= left || target.CursorX > target.Width - right) target.CursorX = Math.Max(1, left + 1);
    }

    public void SetMouseWindow(ushort window)
    {
        MouseWindow = window == ushort.MaxValue ? -1 : window;
        if (MouseWindow != -1) _ = ResolveWindow(MouseWindow);
    }

    private WindowState ResolveWindow(int window)
    {
        var normalized = window >= 0xfffc ? unchecked((short)window) : window;
        var index = normalized == -3 ? SelectedWindow : normalized;
        if (Version != 6 || index is < 0 or > 7) throw new ZMachineRuntimeException($"Window {window} is not defined in version {Version}.");
        return _v6Windows[index];
    }

    private static void SetV6PaletteColour(WindowState target, ushort requested, bool foreground)
    {
        var current = foreground ? target.Foreground : target.Background;
        var value = unchecked((short)requested);
        var selected = value switch
        {
            0 or -1 => current,
            1 => foreground ? 2 : 9,
            >= 2 and <= 12 => value,
            _ => current
        };
        if (foreground)
        {
            target.Foreground = selected;
            target.TrueForeground = TrueColour(selected);
        }
        else
        {
            target.Background = selected;
            target.TrueBackground = TrueColour(selected);
        }
    }

    private static void SetTrueColour(WindowState target, ushort requested, bool foreground)
    {
        var value = unchecked((short)requested);
        if (value == -2 || value == -3 || value == -4) return;
        if (value == -1)
        {
            if (foreground)
            {
                target.TrueForeground = 0;
                target.Foreground = 2;
            }
            else
            {
                target.TrueBackground = 0x7fff;
                target.Background = 9;
            }

            return;
        }

        if (value >= 0 && (requested & 0x8000) == 0)
        {
            if (foreground)
            {
                target.TrueForeground = requested;
                target.Foreground = StandardColourCode(requested);
            }
            else
            {
                target.TrueBackground = requested;
                target.Background = StandardColourCode(requested);
            }
        }
    }

    private static int TrueColour(int code) => code switch
    {
        2 => 0x0000,
        3 => 0x001d,
        4 => 0x0340,
        5 => 0x03bd,
        6 => 0x59a0,
        7 => 0x7c1f,
        8 => 0x77a0,
        9 => 0x7fff,
        10 => 0x5ad6,
        11 => 0x4631,
        12 => 0x2d6b,
        _ => 0
    };

    private static int StandardColourCode(int colour)
    {
        for (var code = 2; code <= 12; code++)
        {
            if (TrueColour(code) == colour) return code;
        }

        return 16;
    }

    public void SetTextStyle(ushort style)
    {
        TextStyle = style;
        if (Version == 6) _v6Windows[SelectedWindow].Style = style;
    }

    public void SetColour(ushort foreground, ushort background)
    {
        if (Version < 5)
        {
            return;
        }

        if (Version == 6)
        {
            SetV6Colour(foreground, background, checked((ushort)SelectedWindow));
            return;
        }
        Foreground = ResolveColour(foreground, Foreground, 2);
        Background = ResolveColour(background, Background, 9);
    }

    public void SetBuffering(bool enabled) => LowerBufferingEnabled = enabled;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var index = 0; index < text.Length; index++)
        {
            var shouldWordWrap = Version == 6
                ? (_v6Windows[SelectedWindow].Attributes & 9) == 9
                : SelectedWindow == 0 && (Version <= 5 || Version >= 7) && (Version <= 3 || LowerBufferingEnabled);
            if (shouldWordWrap && !char.IsWhiteSpace(text[index]))
            {
                var end = index + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                var cursorColumn = Version == 6
                    ? Math.Max(0, (_v6Windows[SelectedWindow].CursorX - _v6Windows[SelectedWindow].LeftMargin - 1) / Math.Max(1, _v6Windows[SelectedWindow].FontWidth))
                    : _lowerCursorColumn;
                var availableWidth = Version == 6
                    ? Math.Max(0, (_v6Windows[SelectedWindow].Width - _v6Windows[SelectedWindow].LeftMargin - _v6Windows[SelectedWindow].RightMargin) / Math.Max(1, _v6Windows[SelectedWindow].FontWidth))
                    : WidthCharacters;
                var remainingWidth = availableWidth - cursorColumn;
                if (cursorColumn > 0 && end - index > remainingWidth)
                {
                    WriteCharacter('\n');
                }
            }

            WriteCharacter(text[index]);
        }
    }

    public void EraseLine(ushort value)
    {
        if (Version == 6)
        {
            if (value != 0) _v6Windows[SelectedWindow].CursorX = Math.Max(1, _v6Windows[SelectedWindow].CursorX);
            return;
        }
        if (Version < 4 || value != 1)
        {
            return;
        }

        var cells = SelectedWindow == 1 ? _upperCells : _lowerCells;
        var row = SelectedWindow == 1 ? _upperCursorRow : _lowerCursorRow;
        var column = SelectedWindow == 1 ? _upperCursorColumn : _lowerCursorColumn;
        Array.Fill(cells[row], ' ', column, WidthCharacters - column);
    }

    public void EraseWindow(short window)
    {
        if (Version == 6)
        {
            if (window == -1)
            {
                UpperWindowHeight = 0;
                _v6Windows[0].Y = _v6Windows[0].X = 1;
                _v6Windows[0].Width = WidthUnits;
                _v6Windows[0].Height = HeightUnits;
                _v6Windows[1].Y = _v6Windows[1].X = 1;
                _v6Windows[1].Width = WidthUnits;
                _v6Windows[1].Height = 0;
                _v6Windows[0].CursorY = _v6Windows[0].CursorX = 1;
                _v6Windows[1].CursorY = _v6Windows[1].CursorX = 1;
                SelectedWindow = 0;
                return;
            }
            if (window == -2) return;
            var target = ResolveWindow(window);
            target.CursorY = 1;
            target.CursorX = Math.Max(1, target.LeftMargin + 1);
            return;
        }

        if (window == -1)
        {
            ClearGrid(_upperCells);
            ClearGrid(_lowerCells);
            UpperWindowHeight = 0;
            SelectedWindow = 0;
            _upperCursorRow = _upperCursorColumn = 0;
            _lowerCursorRow = Version == 4 ? HeightLines - 1 : 0;
            _lowerCursorColumn = 0;
            return;
        }

        if (window == -2)
        {
            ClearGrid(_upperCells);
            ClearGrid(_lowerCells);
            _upperCursorRow = _upperCursorColumn = 0;
            _lowerCursorRow = _lowerCursorColumn = 0;
            return;
        }

        if (window is not (0 or 1))
        {
            throw new ZMachineRuntimeException($"Text window {window} is not defined in version {Version}.");
        }

        ClearGrid(window == 1 ? _upperCells : _lowerCells);
        if (Version >= 5 || window == 1)
        {
            if (window == 1)
            {
                _upperCursorRow = _upperCursorColumn = 0;
            }
            else
            {
                _lowerCursorRow = _lowerCursorColumn = 0;
            }
        }
        else if (Version == 4)
        {
            _lowerCursorRow = HeightLines - 1;
            _lowerCursorColumn = 0;
        }
    }

    private void WriteCharacter(char character)
    {
        if (Version == 6)
        {
            var target = _v6Windows[SelectedWindow];
            var cellWidth = Math.Max(1, target.FontWidth);
            var cellHeight = Math.Max(1, target.FontHeight);
            var maxColumns = Math.Max(0, (target.Width - target.LeftMargin - target.RightMargin) / cellWidth);
            var maxRows = Math.Max(0, target.Height / cellHeight);
            if (character is '\r' or '\n')
            {
                target.CursorY = Math.Min(Math.Max(1, maxRows * cellHeight), target.CursorY + cellHeight);
                target.CursorX = target.LeftMargin + 1;
                if (target.InterruptCountdown != 0 && target.LineCount > -999)
                {
                    target.LineCount--;
                    if (target.LineCount == 0)
                    {
                        target.LineCount = target.InterruptCountdown;
                        if (target.InterruptRoutine != 0) NewlineInterrupt?.Invoke(checked((ushort)target.InterruptRoutine));
                    }
                }
                return;
            }
            if (maxColumns == 0 || target.CursorX > target.Width - target.RightMargin) return;
            target.CursorX += cellWidth;
            if (target.CursorX > target.Width - target.RightMargin && (target.Attributes & 1) != 0)
            {
                target.CursorX = target.LeftMargin + 1;
                if ((target.Attributes & 8) != 0)
                    target.CursorY = Math.Min(Math.Max(1, maxRows * cellHeight), target.CursorY + cellHeight);
            }
            return;
        }

        var upper = Version >= 3 && SelectedWindow == 1;
        var output = upper ? _upperCells : _lowerCells;
        var maximumRow = upper ? UpperWindowHeight : HeightLines;
        if (character == '\r')
        {
            character = '\n';
        }

        if (character == '\n')
        {
            SetCursor(upper, 0, NextRow(upper, maximumRow));
            return;
        }

        var column = upper ? _upperCursorColumn : _lowerCursorColumn;
        if (column >= WidthCharacters)
        {
            if (upper)
            {
                return;
            }

            SetCursor(false, 0, NextRow(false, maximumRow));
            column = 0;
        }

        var row = upper ? _upperCursorRow : _lowerCursorRow;
        output[row][column] = character;
        if (upper)
        {
            _upperCursorColumn++;
        }
        else
        {
            _lowerCursorColumn++;
        }
    }

    private int NextRow(bool upper, int maximumRow)
    {
        var row = upper ? _upperCursorRow : _lowerCursorRow;
        row++;
        if (upper)
        {
            return Math.Min(row, Math.Max(0, maximumRow - 1));
        }

        if (row >= HeightLines)
        {
            row = HeightLines - 1;
            var scrollStart = Version >= 5 ? Math.Min(UpperWindowHeight, HeightLines - 1) : 0;
            for (var line = scrollStart; line < HeightLines - 1; line++)
            {
                _lowerCells[line] = _lowerCells[line + 1];
            }

            _lowerCells[HeightLines - 1] = CreateLine(WidthCharacters);
        }

        return Math.Max(row, Version >= 4 ? UpperWindowHeight : 0);
    }

    private void SetCursor(bool upper, int column, int row)
    {
        if (upper)
        {
            _upperCursorColumn = column;
            _upperCursorRow = row;
        }
        else
        {
            _lowerCursorColumn = column;
            _lowerCursorRow = row;
        }
    }

    private static short ResolveColour(ushort requested, short current, short defaultColour) => requested switch
    {
        0 => current,
        1 => defaultColour,
        >= 2 and <= 9 => (short)requested,
        _ => current
    };

    private static char[][] CreateGrid(int rows, int columns) =>
        Enumerable.Range(0, rows).Select(_ => CreateLine(columns)).ToArray();

    private static char[] CreateLine(int columns)
    {
        var line = new char[columns];
        Array.Fill(line, ' ');
        return line;
    }

    private static char[][] ResizeGrid(char[][] oldGrid, int rows, int columns)
    {
        var resized = CreateGrid(rows, columns);
        for (var row = 0; row < Math.Min(oldGrid.Length, rows); row++)
        {
            Array.Copy(oldGrid[row], resized[row], Math.Min(oldGrid[row].Length, columns));
        }

        return resized;
    }

    private static void ClearGrid(char[][] grid)
    {
        foreach (var line in grid)
        {
            Array.Fill(line, ' ');
        }
    }

    private static string Render(char[][] grid, int visibleRows = int.MaxValue)
    {
        var lines = grid.Take(Math.Min(grid.Length, visibleRows)).Select(line => new string(line).TrimEnd()).ToArray();
        return string.Join('\n', lines).TrimEnd('\n');
    }

    private void ConfigureHeader()
    {
        var flags1 = _memory.ReadByte(1);
        if (Version >= 4)
        {
            flags1 = (byte)(flags1 & ~0x01);
        }
        if (Version == 3)
        {
            flags1 = (byte)(flags1 & ~0x10); // Status line is implemented.
            flags1 |= 0x20; // Upper and lower text windows are implemented.
        }
        else if (Version >= 4)
        {
            flags1 |= 0x1c; // Bold, italic, and fixed-pitch styles are represented by the screen model.
            if (Version >= 5)
            {
                flags1 |= 0x01; // Version 5 color state is exposed to the screen host.
            }
        }

        _memory.WriteByte(1, flags1);
        if (Version == 6)
        {
            // This screen model has no Blorb picture, mouse, or menu provider yet.
            flags1 = (byte)(flags1 & ~0x22); // Pictures and sound are unavailable until their providers are added.
            _memory.WriteByte(1, flags1);
            _memory.WriteWord(0x10, (ushort)(_memory.ReadWord(0x10) & ~((1 << 3) | (1 << 5) | (1 << 8))));
            var extension = _memory.ReadWord(0x36);
            if (extension != 0 && _memory.ReadWord(extension) >= 4)
            {
                var flags3 = checked((ushort)(extension + 8));
                _memory.WriteWord(flags3, (ushort)(_memory.ReadWord(flags3) & ~1));
            }
        }
        if (Version >= 4)
        {
            _memory.WriteByte(0x20, checked((byte)Math.Min(HeightLines, byte.MaxValue)));
            _memory.WriteByte(0x21, checked((byte)Math.Min(WidthCharacters, byte.MaxValue)));
            if (Version >= 5)
            {
                _memory.WriteWord(0x22, checked((ushort)WidthUnits));
                _memory.WriteWord(0x24, checked((ushort)HeightUnits));
                _memory.WriteByte(0x26, Version == 6 ? (byte)16 : (byte)8);
                _memory.WriteByte(0x27, Version == 6 ? (byte)8 : (byte)16);
                _memory.WriteByte(0x2c, 9);
                _memory.WriteByte(0x2d, 2);
            }
        }
    }
}
