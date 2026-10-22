using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Leafpile.Desktop;

public sealed partial class InstructionWindow : Window
{
    private InstructionAuditLog _auditLog = null!;
    private readonly ObservableCollection<InstructionAuditLine> _lines = [];
    private readonly InstructionCommandNavigator _commandNavigator = new();
    private bool _ownerClosing;

    public event Action<int>? HistoryCleared;

    public InstructionWindow()
    {
        InitializeComponent();
        AuditLines.ItemsSource = _lines;
        Closing += OnClosing;
    }

    internal void BindAuditLog(InstructionAuditLog auditLog) => _auditLog = auditLog;

    public void Append(IEnumerable<string> lines)
    {
        var additions = lines.ToArray();
        var latestCommandLineIndex = -1;
        foreach (var line in additions)
        {
            var lineIndex = _lines.Count;
            _lines.Add(new InstructionAuditLine(line));
            if (!line.StartsWith("> ", StringComparison.Ordinal)) continue;
            _commandNavigator.Add(lineIndex);
            latestCommandLineIndex = lineIndex;
        }
        if (latestCommandLineIndex >= 0)
        {
            AuditLines.SelectedIndex = latestCommandLineIndex;
            AuditLines.ScrollIntoView(_lines[latestCommandLineIndex]);
        }
        UpdateCommandNavigationButtons();
    }

    public int ClearDisplayed()
    {
        _lines.Clear();
        _commandNavigator.Reset();
        var currentLines = _auditLog.SnapshotFrom(0);
        Append(currentLines);
        return currentLines.Length;
    }

    private void OnPreviousCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        NavigateToCommand(_commandNavigator.PreviousLineIndex());
    }

    private void OnCurrentCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        NavigateToCommand(_commandNavigator.CurrentLineIndexAndSelect());
    }

    private void OnNextCommand(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        NavigateToCommand(_commandNavigator.NextLineIndex());
    }

    private void NavigateToCommand(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count) return;
        AuditLines.SelectedIndex = lineIndex;
        AuditLines.ScrollIntoView(_lines[lineIndex]);
        UpdateCommandNavigationButtons();
    }

    private void UpdateCommandNavigationButtons()
    {
        PreviousCommandButton.IsEnabled = _commandNavigator.CanGoPrevious;
        CurrentCommandButton.IsEnabled = _commandNavigator.HasCommands;
        NextCommandButton.IsEnabled = _commandNavigator.CanGoNext;
    }

    public void AllowOwnerClose() => _ownerClosing = true;

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        _ = sender;
        if (_ownerClosing) return;
        args.Cancel = true;
        Hide();
    }

    private async void OnSaveAudit(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        ErrorText.Text = string.Empty;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Instruction Audit",
                SuggestedFileName = "leafpile-instruction-audit.txt",
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType("Text document") { Patterns = ["*.txt"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(_auditLog.ExportText());
            await writer.FlushAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"Could not save the instruction audit: {exception.Message}";
        }
    }

    private void OnClearHistory(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        _auditLog.Clear();
        HistoryCleared?.Invoke(ClearDisplayed());
    }
}

internal sealed class InstructionAuditLine(string text)
{
    public string Text { get; } = text;
    public FontWeight FontWeight { get; } = text.StartsWith("> ", StringComparison.Ordinal) ? FontWeight.Bold : FontWeight.Normal;
}
