using System.ComponentModel;
using System.Runtime.CompilerServices;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public sealed record GameDisplayTextSegment(
    string Text,
    short Window,
    ZMachineTextStyle Style,
    ushort FontNumber,
    ushort Foreground,
    ushort Background,
    bool HasExplicitColours,
    bool HasTrueColours);

/// <summary>Bindable presentation state for the scrolling story display and its prompt.</summary>
public sealed class GameDisplayModel : INotifyPropertyChanged
{
    private string _location = string.Empty;
    private string _rightStatus = "Score: 0  Moves: 0";
    private string _transcript = string.Empty;
    private string _inputText = string.Empty;
    private string _promptText = ">";
    private string _upperWindowText = string.Empty;
    private int _upperWindowHeight;
    private int _upperWindowWidth = 80;
    private int _upperWindowRow = 1;
    private int _upperWindowColumn = 1;
    private int _storyVersion = 5;
    private readonly List<List<string>> _upperWindowRows = [];
    private bool _isUpperWindowVisible;
    private bool _isStatusStripVisible;
    private bool _isMorePromptVisible;
    private bool _hasStoryLoaded;
    private short _selectedWindow;
    private ZMachineTextStyle _textStyle;
    private ushort _fontNumber = ZMachineFontCatalog.NormalFont;
    private ushort _foreground = 9;
    private ushort _background = 2;
    private bool _hasExplicitColours;
    private bool _hasTrueColours;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? CommandSubmitted;

    public event Action<GameDisplayTextSegment>? OutputSegmentAdded;

    public event Action? OutputCleared;

    public event Action? PresentationReset;

    public string Location
    {
        get => _location;
        private set => SetProperty(ref _location, value);
    }

    public string RightStatus
    {
        get => _rightStatus;
        private set => SetProperty(ref _rightStatus, value);
    }

    public string Transcript
    {
        get => _transcript;
        private set => SetProperty(ref _transcript, value);
    }

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    public string PromptText
    {
        get => _promptText;
        private set => SetProperty(ref _promptText, value);
    }

    public string UpperWindowText
    {
        get => _upperWindowText;
        private set => SetProperty(ref _upperWindowText, value);
    }

    public bool IsUpperWindowVisible
    {
        get => _isUpperWindowVisible;
        private set => SetProperty(ref _isUpperWindowVisible, value);
    }

    public bool IsStatusStripVisible
    {
        get => _isStatusStripVisible;
        private set => SetProperty(ref _isStatusStripVisible, value);
    }

    public int UpperWindowHeight
    {
        get => _upperWindowHeight;
        private set => SetProperty(ref _upperWindowHeight, value);
    }

    public bool IsMorePromptVisible
    {
        get => _isMorePromptVisible;
        private set => SetProperty(ref _isMorePromptVisible, value);
    }

    public bool HasStoryLoaded
    {
        get => _hasStoryLoaded;
        private set => SetProperty(ref _hasStoryLoaded, value);
    }

    public short SelectedWindow
    {
        get => _selectedWindow;
        private set => SetProperty(ref _selectedWindow, value);
    }

    public void SetStatusLine(string location, string rightStatus)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(rightStatus);
        Location = location;
        RightStatus = rightStatus;
    }

    public void AppendOutput(string text, short window = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (window == 1)
        {
            if (_storyVersion >= 6)
            {
                UpperWindowText += text;
            }
            else
            {
                AppendUpperWindowText(text);
            }

            IsUpperWindowVisible = true;
        }
        else
        {
            Transcript += text;
            OutputSegmentAdded?.Invoke(new GameDisplayTextSegment(
                text,
                window,
                _textStyle,
                _fontNumber,
                _foreground,
                _background,
                _hasExplicitColours,
                _hasTrueColours));
        }
    }

    public void ClearOutput()
    {
        Transcript = string.Empty;
        ClearUpperWindow();
        UpperWindowHeight = 0;
        ResizeUpperWindowRows(0);
        IsUpperWindowVisible = false;
        SelectedWindow = 0;
        OutputCleared?.Invoke();
    }

    public void ClearWindow(short window)
    {
        if (window == 1)
        {
            if (_storyVersion >= 6)
            {
                UpperWindowText = string.Empty;
            }
            else
            {
                ClearUpperWindow();
            }
        }
        else
        {
            Transcript = string.Empty;
            OutputCleared?.Invoke();
        }
    }

    public void SetSelectedWindow(short window)
    {
        SelectedWindow = window;
        if (window == 1)
        {
            _upperWindowRow = 1;
            _upperWindowColumn = 1;
        }
    }

    public void SplitWindow(int height)
    {
        if (_storyVersion >= 6)
        {
            IsUpperWindowVisible = height > 0;
            return;
        }

        UpperWindowHeight = Math.Max(0, height);
        ResizeUpperWindowRows(UpperWindowHeight);
        IsUpperWindowVisible = height > 0;
    }

    public void SetStoryVersion(int version)
    {
        _storyVersion = version;
        UpdateStatusStripVisibility();
    }

    public void SetUpperWindowWidth(int width) => _upperWindowWidth = Math.Max(1, width);

    public void MoveCursor(short window, int row, int column)
    {
        if (window != 1 || _storyVersion >= 6)
        {
            return;
        }

        _upperWindowRow = Math.Max(1, row);
        _upperWindowColumn = Math.Max(1, column);
        EnsureUpperWindowRow(_upperWindowRow);
    }

    public void EraseLine(short window, int row, int column)
    {
        if (window != 1 || _storyVersion >= 6)
        {
            return;
        }

        EnsureUpperWindowRow(row);
        var line = _upperWindowRows[row - 1];
        for (var index = Math.Max(1, column) - 1; index < line.Count; index++)
        {
            line[index] = " ";
        }

        RefreshUpperWindowText();
    }

    public void SetTextStyle(ushort style) => _textStyle = (ZMachineTextStyle)style;

    public void SetFont(ushort fontNumber) => _fontNumber = fontNumber;

    public void SetColours(ushort packedColours)
    {
        _foreground = (ushort)(packedColours >> 8);
        _background = (byte)packedColours;
        _hasExplicitColours = true;
        _hasTrueColours = false;
    }

    private void AppendUpperWindowText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value is '\r' or '\n')
            {
                AdvanceUpperWindowLine();
                continue;
            }

            if (_upperWindowColumn > _upperWindowWidth)
            {
                AdvanceUpperWindowLine();
            }

            EnsureUpperWindowRow(_upperWindowRow);
            var line = _upperWindowRows[_upperWindowRow - 1];
            while (line.Count < _upperWindowColumn)
            {
                line.Add(" ");
            }

            line[_upperWindowColumn - 1] = rune.ToString();
            _upperWindowColumn++;
            if (_upperWindowColumn > _upperWindowWidth)
            {
                AdvanceUpperWindowLine();
            }
        }

        RefreshUpperWindowText();
    }

    private void AdvanceUpperWindowLine()
    {
        _upperWindowColumn = 1;
        if (_upperWindowRow < _upperWindowHeight)
        {
            _upperWindowRow++;
        }

        EnsureUpperWindowRow(_upperWindowRow);
    }

    private void ClearUpperWindow()
    {
        foreach (var line in _upperWindowRows)
        {
            line.Clear();
        }

        _upperWindowRow = 1;
        _upperWindowColumn = 1;
        RefreshUpperWindowText();
    }

    private void ResizeUpperWindowRows(int height)
    {
        while (_upperWindowRows.Count < height)
        {
            _upperWindowRows.Add([]);
        }

        if (_upperWindowRows.Count > height)
        {
            _upperWindowRows.RemoveRange(height, _upperWindowRows.Count - height);
        }

        _upperWindowRow = Math.Clamp(_upperWindowRow, 1, Math.Max(1, height));
        RefreshUpperWindowText();
    }

    private void EnsureUpperWindowRow(int row)
    {
        var rowCount = Math.Max(UpperWindowHeight, row);
        while (_upperWindowRows.Count < rowCount)
        {
            _upperWindowRows.Add([]);
        }
    }

    private void RefreshUpperWindowText()
    {
        var renderedRows = _upperWindowRows.Select(line => string.Concat(line).TrimEnd()).ToArray();
        var lastContentRow = Array.FindLastIndex(renderedRows, row => !string.IsNullOrEmpty(row));
        UpperWindowText = lastContentRow < 0
            ? string.Empty
            : string.Join('\n', renderedRows.Take(lastContentRow + 1));
    }

    public void SetTrueColours(ushort foreground, ushort background)
    {
        _foreground = foreground;
        _background = background;
        _hasExplicitColours = true;
        _hasTrueColours = true;
    }

    public void ShowMorePrompt(bool visible)
    {
        IsMorePromptVisible = visible;
        PromptText = visible ? "[MORE]" : ">";
    }

    public void SetStoryLoaded(bool loaded)
    {
        HasStoryLoaded = loaded;
        UpdateStatusStripVisibility();
    }

    private void UpdateStatusStripVisibility() => IsStatusStripVisible = HasStoryLoaded && _storyVersion is >= 1 and <= 3;

    public void ResetPresentation()
    {
        PresentationReset?.Invoke();
        ClearOutput();
        InputText = string.Empty;
        SetStatusLine(string.Empty, "Score: 0  Moves: 0");
        SetStoryLoaded(loaded: false);
        ShowMorePrompt(visible: false);
        UpperWindowHeight = 0;
        ResizeUpperWindowRows(0);
        IsUpperWindowVisible = false;
        SelectedWindow = 0;
        _textStyle = ZMachineTextStyle.Roman;
        _fontNumber = ZMachineFontCatalog.NormalFont;
        _foreground = 9;
        _background = 2;
        _hasExplicitColours = false;
        _hasTrueColours = false;
    }

    public void ResetAfterRestore()
    {
        PresentationReset?.Invoke();
        ClearWindow(0);
        InputText = string.Empty;
        ShowMorePrompt(visible: false);
    }

    public void SubmitCommand()
    {
        var command = InputText;
        InputText = string.Empty;
        CommandSubmitted?.Invoke(command);
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
