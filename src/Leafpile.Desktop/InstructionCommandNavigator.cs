namespace Leafpile.Desktop;

internal sealed class InstructionCommandNavigator
{
    private readonly List<int> _commandLineIndices = [];
    private int _selectedPosition = -1;

    public bool CanGoPrevious => _selectedPosition > 0;
    public bool HasCommands => _commandLineIndices.Count > 0;
    public bool CanGoNext => _selectedPosition >= 0 && _selectedPosition < _commandLineIndices.Count - 1;
    public int CurrentLineIndex => HasCommands ? _commandLineIndices[^1] : -1;

    public void Add(int lineIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineIndex);
        _commandLineIndices.Add(lineIndex);
        _selectedPosition = _commandLineIndices.Count - 1;
    }

    public int PreviousLineIndex()
    {
        if (CanGoPrevious) _selectedPosition--;
        return SelectedLineIndex;
    }

    public int CurrentLineIndexAndSelect()
    {
        if (HasCommands) _selectedPosition = _commandLineIndices.Count - 1;
        return SelectedLineIndex;
    }

    public int NextLineIndex()
    {
        if (CanGoNext) _selectedPosition++;
        return SelectedLineIndex;
    }

    public void Reset()
    {
        _commandLineIndices.Clear();
        _selectedPosition = -1;
    }

    private int SelectedLineIndex => _selectedPosition < 0 ? -1 : _commandLineIndices[_selectedPosition];
}
