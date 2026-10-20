using Leafpile.Core.Loading;

namespace Leafpile.Core.Machine;

/// <summary>Tracks the text screen and versioned window state exposed by the Z-machine.</summary>
public sealed class ZMachineScreen
{
    private readonly ZMachineMemory _memory;
    private char[][] _lowerCells;
    private char[][] _upperCells;
    private int _upperCursorRow;
    private int _upperCursorColumn;
    private int _lowerCursorRow;
    private int _lowerCursorColumn;

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

    public int CursorRow => SelectedWindow == 1 ? _upperCursorRow : _lowerCursorRow;

    public int CursorColumn => SelectedWindow == 1 ? _upperCursorColumn : _lowerCursorColumn;

    public int TextStyle { get; private set; }

    public short Foreground { get; private set; }

    public short Background { get; private set; }

    public bool LowerBufferingEnabled { get; private set; }

    public bool UsesFixedPitch =>
        Version <= 5 && ((SelectedWindow == 1 && Version >= 4) || (TextStyle & 0x08) != 0 || (_memory.ReadWord(0x10) & 0x02) != 0);

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

        WidthCharacters = columns;
        HeightLines = rows;
        WidthUnits = widthUnits;
        HeightUnits = heightUnits;
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

    public void SetTextStyle(ushort style)
    {
        TextStyle = style;
    }

    public void SetColour(ushort foreground, ushort background)
    {
        if (Version < 5)
        {
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
            if (SelectedWindow == 0 && (Version <= 3 || LowerBufferingEnabled) && !char.IsWhiteSpace(text[index]))
            {
                var end = index + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                var remainingWidth = WidthCharacters - _lowerCursorColumn;
                if (_lowerCursorColumn > 0 && end - index > remainingWidth)
                {
                    WriteCharacter('\n');
                }
            }

            WriteCharacter(text[index]);
        }
    }

    public void EraseLine(ushort value)
    {
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
        if (Version >= 4)
        {
            _memory.WriteByte(0x20, checked((byte)Math.Min(HeightLines, byte.MaxValue)));
            _memory.WriteByte(0x21, checked((byte)Math.Min(WidthCharacters, byte.MaxValue)));
            if (Version >= 5)
            {
                _memory.WriteWord(0x22, checked((ushort)WidthUnits));
                _memory.WriteWord(0x24, checked((ushort)HeightUnits));
                _memory.WriteByte(0x26, 8);
                _memory.WriteByte(0x27, 16);
                _memory.WriteByte(0x2c, 9);
                _memory.WriteByte(0x2d, 2);
            }
        }
    }
}
