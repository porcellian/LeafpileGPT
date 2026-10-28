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
    private bool _isUpperWindowVisible;
    private bool _isMorePromptVisible;
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

    public bool IsMorePromptVisible
    {
        get => _isMorePromptVisible;
        private set => SetProperty(ref _isMorePromptVisible, value);
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
            UpperWindowText += text;
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
        UpperWindowText = string.Empty;
        OutputCleared?.Invoke();
    }

    public void ClearWindow(short window)
    {
        if (window == 1)
        {
            UpperWindowText = string.Empty;
        }
        else
        {
            Transcript = string.Empty;
            OutputCleared?.Invoke();
        }
    }

    public void SetSelectedWindow(short window) => SelectedWindow = window;

    public void SplitWindow(int height) => IsUpperWindowVisible = height > 0;

    public void SetTextStyle(ushort style) => _textStyle = (ZMachineTextStyle)style;

    public void SetFont(ushort fontNumber) => _fontNumber = fontNumber;

    public void SetColours(ushort packedColours)
    {
        _foreground = (ushort)(packedColours >> 8);
        _background = (byte)packedColours;
        _hasExplicitColours = true;
        _hasTrueColours = false;
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
        PromptText = visible ? "-- MORE --" : ">";
    }

    public void ResetPresentation()
    {
        PresentationReset?.Invoke();
        ClearOutput();
        InputText = string.Empty;
        SetStatusLine(string.Empty, "Score: 0  Moves: 0");
        ShowMorePrompt(visible: false);
        IsUpperWindowVisible = false;
        SelectedWindow = 0;
        _textStyle = ZMachineTextStyle.Roman;
        _fontNumber = ZMachineFontCatalog.NormalFont;
        _foreground = 9;
        _background = 2;
        _hasExplicitColours = false;
        _hasTrueColours = false;
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
