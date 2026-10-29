using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Read-only, live view of commands and their executed VM instructions.</summary>
public sealed class InstructionAuditWindow : Window
{
    private readonly string _auditsDirectory;
    private readonly ObservableCollection<AuditDisplayLine> _lines = [];
    private readonly ListBox _instructionList;
    private readonly TextBlock _groupHeading;
    private readonly TextBlock _status;
    private readonly Button _previousButton;
    private readonly Button _currentButton;
    private readonly Button _nextButton;
    private readonly ZMachineInstructionAuditNavigation _navigation = new();
    private DesktopGameSession? _session;
    private string _storyName = "game";
    private long? _renderedGroupNumber;
    private int _renderedInstructionCount;
    private int _refreshPosted;

    public InstructionAuditWindow(string auditsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditsDirectory);
        _auditsDirectory = auditsDirectory;
        Title = "View Instructions";
        Width = 900;
        Height = 620;
        MinWidth = 640;
        MinHeight = 400;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var saveButton = new Button { Content = "Save Audit" };
        saveButton.Click += async (_, _) => await SaveAuditAsync();
        var clearButton = new Button { Content = "Clear History" };
        _previousButton = new Button { Content = "Previous" };
        _previousButton.Click += (_, _) => NavigatePrevious();
        _currentButton = new Button { Content = "Current" };
        _currentButton.Click += (_, _) => NavigateCurrent();
        _nextButton = new Button { Content = "Next" };
        _nextButton.Click += (_, _) => NavigateNext();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { saveButton, clearButton, _previousButton, _currentButton, _nextButton }
        };
        _groupHeading = new TextBlock { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        _instructionList = new ListBox
        {
            ItemsSource = _lines,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<AuditDisplayLine>((line, _) => new TextBlock
            {
                Text = line?.Text ?? string.Empty,
                FontFamily = new FontFamily("Courier New, Courier, monospace"),
                FontSize = 12,
                FontWeight = line?.IsHeading == true ? FontWeight.Bold : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap
            })
        };
        _status = new TextBlock { Text = "Recording begins when a story is loaded.", Foreground = Brushes.DimGray };
        clearButton.Click += (_, _) =>
        {
            _session?.InstructionAudit.ClearHistory();
            _status.Text = "Unsaved instruction history cleared.";
        };

        var layout = new Grid
        {
            Margin = new Avalonia.Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Children = { toolbar, _groupHeading, _instructionList, _status }
        };
        Grid.SetRow(_groupHeading, 1);
        Grid.SetRow(_instructionList, 2);
        Grid.SetRow(_status, 3);
        Content = layout;
        Closed += (_, _) => AttachSession(null, string.Empty);
    }

    public void SetSession(DesktopGameSession? session, string? storyName)
    {
        AttachSession(session, storyName ?? string.Empty);
        _renderedGroupNumber = null;
        _renderedInstructionCount = 0;
        _navigation.Current(session?.InstructionAudit.GroupCount ?? 0);
        Refresh();
    }

    private void AttachSession(DesktopGameSession? session, string storyName)
    {
        if (_session is not null)
        {
            _session.InstructionAudit.Changed -= OnAuditChanged;
        }

        _session = session;
        _storyName = string.IsNullOrWhiteSpace(storyName) ? "game" : Path.GetFileNameWithoutExtension(storyName);
        if (_session is not null)
        {
            _session.InstructionAudit.Changed += OnAuditChanged;
            _status.Text = "Live instruction capture is active.";
        }
        else
        {
            _status.Text = "Recording begins when a story is loaded.";
        }
    }

    private void OnAuditChanged()
    {
        if (Interlocked.Exchange(ref _refreshPosted, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshPosted, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        var audit = _session?.InstructionAudit;
        var count = audit?.GroupCount ?? 0;
        _navigation.GroupsChanged(count);
        var group = audit?.GetGroupInfo(_navigation.SelectedIndex);
        _groupHeading.Text = group is null
            ? "No instruction group selected."
            : $"Command {_navigation.SelectedIndex + 1} of {count}: {DisplayCommand(group.Command)}";

        if (group is null)
        {
            _lines.Clear();
            _renderedGroupNumber = null;
            _renderedInstructionCount = 0;
        }
        else
        {
            if (_renderedGroupNumber != group.Number)
            {
                _lines.Clear();
                _lines.Add(new AuditDisplayLine($"Group {group.Number} - {group.StartedAt:yyyy-MM-dd HH:mm:ss zzz}", true));
                _renderedGroupNumber = group.Number;
                _renderedInstructionCount = 0;
            }

            if (_lines.Count == 1 && group.InstructionCount == 0)
            {
                _lines.Add(new AuditDisplayLine("No instructions recorded for this command yet.", false));
            }
            else if (_lines.Count == 2 && _lines[1].Text == "No instructions recorded for this command yet.")
            {
                _lines.RemoveAt(1);
            }

            var newInstructions = audit!.GetGroupSnapshot(_navigation.SelectedIndex, _renderedInstructionCount)?.Instructions ?? [];
            foreach (var instruction in newInstructions)
            {
                _lines.Add(new AuditDisplayLine(FormatInstruction(instruction), false));
                _renderedInstructionCount++;
            }

            if (_navigation.IsFollowing && _lines.Count > 0)
            {
                _instructionList.SelectedIndex = _lines.Count - 1;
            }
        }

        _previousButton.IsEnabled = _navigation.SelectedIndex > 0;
        _nextButton.IsEnabled = _navigation.SelectedIndex >= 0 && _navigation.SelectedIndex < count - 1;
        _currentButton.IsEnabled = count > 0;
    }

    private void NavigatePrevious()
    {
        _navigation.Previous(_session?.InstructionAudit.GroupCount ?? 0);
        Refresh();
    }

    private void NavigateCurrent()
    {
        _navigation.Current(_session?.InstructionAudit.GroupCount ?? 0);
        Refresh();
    }

    private void NavigateNext()
    {
        _navigation.Next(_session?.InstructionAudit.GroupCount ?? 0);
        Refresh();
    }

    private async Task SaveAuditAsync()
    {
        if (_session is null)
        {
            _status.Text = "Load a story before saving an instruction audit.";
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            _status.Text = "A file picker is not available for this window.";
            return;
        }

        try
        {
            Directory.CreateDirectory(_auditsDirectory);
            var pdfType = new FilePickerFileType("PDF audit") { Patterns = ["*.pdf"] };
            var options = new FilePickerSaveOptions
            {
                Title = "Save Instruction Audit",
                SuggestedFileName = $"{_storyName}-audit.pdf",
                DefaultExtension = "pdf",
                FileTypeChoices = [pdfType],
                SuggestedFileType = pdfType,
                ShowOverwritePrompt = true
            };
            options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(_auditsDirectory);
            using var selected = await storage.SaveFilePickerAsync(options);
            var path = selected?.TryGetLocalPath();
            if (path is null)
            {
                return;
            }

            var jsonLinesPath = Path.ChangeExtension(path, ".jsonl");
            if (File.Exists(jsonLinesPath) && !File.Exists(path))
            {
                _status.Text = $"The companion audit file already exists: {Path.GetFileName(jsonLinesPath)}. Choose another filename.";
                return;
            }

            var exported = await InstructionAuditExporter.ExportAsync(path, _storyName, _session.InstructionAudit);
            _status.Text = $"Audit saved: {Path.GetFileName(exported.PdfPath)} and {Path.GetFileName(exported.JsonLinesPath)}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _status.Text = $"Unable to save audit: {exception.Message}";
        }
    }

    private static string DisplayCommand(string command) => command.Length == 0 ? "[empty command]" : command;

    private static string FormatInstruction(ZMachineInstructionAuditEntry instruction)
    {
        var operands = string.Join(", ", instruction.Operands.Select(operand => operand.Encoding switch
        {
            OperandEncoding.Variable => $"var[{operand.Value:X2}]",
            OperandEncoding.SmallConstant => $"#{operand.Value:X2}",
            OperandEncoding.LargeConstant => $"#{operand.Value:X4}",
            _ => operand.Value.ToString()
        }));
        var details = string.IsNullOrEmpty(operands) ? string.Empty : $" {operands}";
        if (instruction.StoreVariable is { } store)
        {
            details += $" -> store {store:X2}";
        }

        if (instruction.Branch is { } branch)
        {
            details += branch.TargetAddress is { } target
                ? $"; {(branch.BranchOnTrue ? "true" : "false")} -> 0x{target:X6}"
                : $"; return {branch.ReturnValue}";
        }

        return $"{instruction.Sequence,7} 0x{instruction.ProgramCounter:X6} {instruction.Form}/{instruction.OpcodeClass}:{instruction.Opcode:X2} {instruction.Mnemonic}{details}  next 0x{instruction.ProgramCounterAfter:X6}{(instruction.IsHalted ? " [halted]" : string.Empty)}";
    }

    private sealed record AuditDisplayLine(string Text, bool IsHeading);
}
