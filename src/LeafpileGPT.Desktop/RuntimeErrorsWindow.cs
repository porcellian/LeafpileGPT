using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Read-only history of runtime diagnostics with copyable execution context.</summary>
public sealed class RuntimeErrorsWindow : Window
{
    private readonly ZMachineRuntimeErrorManager _manager;
    private readonly ObservableCollection<ZMachineRuntimeErrorRecord> _records = [];
    private readonly ListBox _list;
    private readonly TextBox _details;

    public RuntimeErrorsWindow(ZMachineRuntimeErrorManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        Title = "Runtime Errors";
        Width = 900;
        Height = 560;
        MinWidth = 600;
        MinHeight = 360;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox
        {
            ItemsSource = _records,
            ItemTemplate = new FuncDataTemplate<ZMachineRuntimeErrorRecord>((record, _) => new TextBlock
            {
                Text = record is null
                    ? string.Empty
                    : $"{record.Timestamp:HH:mm:ss}  {record.Severity}  {record.Mnemonic ?? "VM"} at 0x{record.ProgramCounter:X6}: {record.Message}",
                TextWrapping = TextWrapping.Wrap
            })
        };
        _list.SelectionChanged += (_, _) => UpdateDetails();

        _details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Courier New, Courier, monospace"),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var copyButton = new Button
        {
            Content = "Copy Details",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        copyButton.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(_details.Text ?? string.Empty);
            }
        };

        var detailPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Avalonia.Thickness(8),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Diagnostic details", FontWeight = FontWeight.Bold },
                _details,
                copyButton
            }
        };
        Grid.SetRow(_details, 1);
        Grid.SetRow(copyButton, 2);

        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Children = { _list, detailPanel }
        };
        Grid.SetColumn(detailPanel, 1);

        foreach (var record in _manager.GetHistory())
        {
            _records.Add(record);
        }

        _manager.DiagnosticAdded += OnDiagnosticAdded;
        Closed += (_, _) => _manager.DiagnosticAdded -= OnDiagnosticAdded;
        if (_records.Count > 0)
        {
            _list.SelectedIndex = _records.Count - 1;
        }
    }

    private void OnDiagnosticAdded(ZMachineRuntimeErrorRecord record) => Dispatcher.UIThread.Post(() =>
    {
        if (_records.Count >= 1_000)
        {
            _records.RemoveAt(0);
        }

        _records.Add(record);
        if (_list.SelectedIndex == _records.Count - 2 || _records.Count == 1)
        {
            _list.SelectedIndex = _records.Count - 1;
        }
    });

    private void UpdateDetails() => _details.Text = _list.SelectedItem is ZMachineRuntimeErrorRecord record
        ? record.CopyableDetails
        : string.Empty;
}
