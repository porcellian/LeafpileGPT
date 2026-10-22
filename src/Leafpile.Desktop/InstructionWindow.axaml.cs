using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace Leafpile.Desktop;

public sealed partial class InstructionWindow : Window
{
    private InstructionAuditLog _auditLog = null!;
    private readonly ObservableCollection<string> _lines = [];
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
        var scrollViewer = AuditLines.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var wasAtBottom = scrollViewer is null || scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 2;
        foreach (var line in lines) _lines.Add(line);
        if (wasAtBottom && _lines.Count > 0) AuditLines.ScrollIntoView(_lines[^1]);
    }

    public int ClearDisplayed()
    {
        _lines.Clear();
        var currentLines = _auditLog.SnapshotFrom(0);
        Append(currentLines);
        return currentLines.Length;
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
