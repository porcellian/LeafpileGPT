using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LeafpileGPT.Desktop;

/// <summary>Bindable presentation state for the scrolling story display and its prompt.</summary>
public sealed class GameDisplayModel : INotifyPropertyChanged
{
    private string _location = string.Empty;
    private string _rightStatus = "Score: 0  Moves: 0";
    private string _transcript = string.Empty;
    private string _inputText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? CommandSubmitted;

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

    public void SetStatusLine(string location, string rightStatus)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(rightStatus);
        Location = location;
        RightStatus = rightStatus;
    }

    public void AppendOutput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Transcript += text;
    }

    public void ClearOutput() => Transcript = string.Empty;

    public void SubmitCommand()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

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
