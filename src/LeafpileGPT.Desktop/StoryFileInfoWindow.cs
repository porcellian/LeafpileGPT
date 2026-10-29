using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Holds the current immutable story-info snapshot for the window refresh path.</summary>
public sealed class StoryFileInfoWindowModel
{
    public StoryFileInfoSnapshot? Snapshot { get; private set; }

    public bool HasStory => Snapshot is not null;

    public void SetSnapshot(StoryFileInfoSnapshot? snapshot) => Snapshot = snapshot;
}

/// <summary>Resizable, read-only display of an immutable story-information snapshot.</summary>
public sealed class StoryFileInfoWindow : Window
{
    private readonly StackPanel _sections = new() { Spacing = 12 };
    private readonly StoryFileInfoWindowModel _model = new();
    private readonly TextBlock _title;

    public StoryFileInfoWindow()
    {
        Title = "Story File Info";
        Width = 1040;
        Height = 720;
        MinWidth = 760;
        MinHeight = 480;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _title = new TextBlock
        {
            Text = "No story loaded.",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        var scroll = new ScrollViewer
        {
            Content = _sections,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var body = new Grid
        {
            Margin = new Avalonia.Thickness(14),
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12,
            Children = { _title, scroll }
        };
        Grid.SetRow(scroll, 1);
        Content = body;
        ShowEmptyState();
    }

    public StoryFileInfoSnapshot? Snapshot => _model.Snapshot;

    public bool HasStory => _model.HasStory;

    public void SetSnapshot(StoryFileInfoSnapshot? snapshot)
    {
        _model.SetSnapshot(snapshot);
        if (snapshot is null)
        {
            Title = "Story File Info";
            ShowEmptyState();
            return;
        }

        Title = $"Story File Info - {snapshot.SourceFileName}";
        _title.Text = $"{snapshot.SourceFileName} - Z-machine version {snapshot.Version}";
        _sections.Children.Clear();
        AddSourceDetails(snapshot);
        AddStoryIdentity(snapshot);
        AddValidationDetails(snapshot);
        AddMemoryLayout(snapshot);
        AddHeaderFields(snapshot);
        AddRawHeader("Original on-disk header (64 bytes)", snapshot.OriginalRawHeaderLines);
        AddRawHeader("Current runtime header (64 bytes)", snapshot.RuntimeRawHeaderLines);
        AddBlorbDetails(snapshot);
    }

    private void ShowEmptyState()
    {
        _title.Text = "No story loaded.";
        _sections.Children.Clear();
        _sections.Children.Add(new TextBlock
        {
            Text = "Load a story to view its file identity, header, memory layout, and optional Blorb metadata.",
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void AddSourceDetails(StoryFileInfoSnapshot snapshot)
    {
        AddSection("File identity");
        AddValue("Source file", snapshot.SourceFileName);
        AddValue("Last modified (UTC)", snapshot.SourceFileLastModifiedUtc is { } lastModified
            ? lastModified.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : "Not available");
        AddValue("Path", snapshot.SourcePath);
        AddValue("Format", snapshot.SourceFormat);
        AddValue("Source file size", $"{snapshot.SourceFileSizeBytes:N0} bytes");
        AddValue("Source SHA-256", snapshot.SourceFileSha256);
        AddValue("Loaded story image size", $"{snapshot.LoadedStorySizeBytes:N0} bytes");
        AddValue("Loaded story image SHA-256", snapshot.LoadedStorySha256);
    }

    private void AddStoryIdentity(StoryFileInfoSnapshot snapshot)
    {
        AddSection("Story identity");
        AddValue("Z-machine version", snapshot.Version.ToString());
        AddValue("Release number", snapshot.ReleaseNumber.ToString());
        AddValue("Serial number", snapshot.SerialNumber);
        AddValue("IFID", snapshot.Ifid);
        AddValue("Title", snapshot.Title);
        AddValue("Author", snapshot.Author);
    }

    private void AddValidationDetails(StoryFileInfoSnapshot snapshot)
    {
        AddSection("Size and checksum");
        AddValue("Declared story size", snapshot.DeclaredLengthWord is { } word
            ? $"{word} header units; {snapshot.DeclaredStorySizeBytes:N0} bytes"
            : "Not present; effective byte size " + snapshot.EffectiveStorySizeBytes.ToString("N0"));
        AddValue("Effective story size", $"{snapshot.EffectiveStorySizeBytes:N0} bytes");
        AddValue("Checksum status", snapshot.ChecksumStatus);
        AddValue("Stored checksum", snapshot.StoredChecksum is { } stored ? $"0x{stored:X4}" : "Not present");
        AddValue("Calculated checksum", snapshot.CalculatedChecksum is { } calculated ? $"0x{calculated:X4}" : "Not present");
    }

    private void AddMemoryLayout(StoryFileInfoSnapshot snapshot)
    {
        AddSection("Memory and execution layout");
        AddValue("Dynamic memory", $"0x000000 through 0x{snapshot.DynamicMemoryEndExclusive - 1:X6} (end-exclusive 0x{snapshot.DynamicMemoryEndExclusive:X6})");
        AddValue("Static memory", snapshot.StaticMemoryStart == snapshot.HighMemoryStart
            ? $"empty region at 0x{snapshot.StaticMemoryStart:X6}"
            : $"0x{snapshot.StaticMemoryStart:X6} through 0x{snapshot.HighMemoryStart - 1:X6} (end-exclusive 0x{snapshot.HighMemoryStart:X6})");
        AddValue("High memory", snapshot.HighMemoryStart == snapshot.StoryImageEndExclusive
            ? $"empty region at 0x{snapshot.HighMemoryStart:X6}"
            : $"0x{snapshot.HighMemoryStart:X6} through 0x{snapshot.StoryImageEndExclusive - 1:X6}");
        AddValue("Initial program counter", snapshot.UsesPackedV6MainRoutine
            ? $"packed 0x{snapshot.InitialProgramCounterValue:X4}; resolved byte address 0x{snapshot.InitialProgramCounterByteAddress:X6}"
            : $"byte address 0x{snapshot.InitialProgramCounterByteAddress:X6}");
        AddValue("Dictionary table", FormatAddress(snapshot.DictionaryAddress));
        AddValue("Object table", FormatAddress(snapshot.ObjectTableAddress));
        AddValue("Global variables table", FormatAddress(snapshot.GlobalsTableAddress));
        AddValue("Abbreviations table", FormatAddress(snapshot.AbbreviationsTableAddress));
        AddValue("Alphabet table", FormatOptionalAddress(snapshot.AlphabetTableAddress));
        AddValue("Header extension table", FormatOptionalAddress(snapshot.HeaderExtensionTableAddress));
    }

    private void AddHeaderFields(StoryFileInfoSnapshot snapshot)
    {
        AddSection("Story header: original and runtime values");
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*,190,190"),
            ColumnSpacing = 1,
            RowSpacing = 1
        };
        AddTableCell(grid, 0, 0, "Field", header: true);
        AddTableCell(grid, 0, 1, "Meaning", header: true);
        AddTableCell(grid, 0, 2, "Original (on disk)", header: true);
        AddTableCell(grid, 0, 3, "Current (runtime)", header: true);
        for (var index = 0; index < snapshot.HeaderFields.Length; index++)
        {
            var row = index + 1;
            var field = snapshot.HeaderFields[index];
            AddTableCell(grid, row, 0, field.Name, header: false);
            AddTableCell(grid, row, 1, field.Meaning, header: false);
            AddTableCell(grid, row, 2, field.OriginalValue, header: false);
            AddTableCell(grid, row, 3, field.RuntimeValue, header: false);
        }

        _sections.Children.Add(grid);
    }

    private void AddRawHeader(string heading, IReadOnlyList<string> lines)
    {
        AddSection(heading);
        _sections.Children.Add(new TextBlock
        {
            Text = string.Join(Environment.NewLine, lines),
            FontFamily = new FontFamily("Courier New, Courier, monospace"),
            FontSize = 12
        });
    }

    private void AddBlorbDetails(StoryFileInfoSnapshot snapshot)
    {
        AddSection("Blorb container");
        if (!snapshot.IsBlorb)
        {
            AddValue("Container", "Not present (standalone story file)");
            return;
        }

        AddValue("Container size", $"{snapshot.SourceFileSizeBytes:N0} bytes");
        AddValue("IFhd game identifier", snapshot.BlorbGameIdentifier);
        AddValue("IFmd metadata", snapshot.IfmdStatus);
        AddValue("Release number (RelN)", snapshot.BlorbReleaseNumber);
        AddValue("Frontispiece resource (Fspc)", snapshot.Frontispiece);
        AddValue("Selected executable", snapshot.SelectedExecutable);
        AddValue("Resource counts by chunk type", snapshot.ResourceCounts.IsDefaultOrEmpty
            ? "None"
            : string.Join(", ", snapshot.ResourceCounts.Select(item => $"{DisplayFourCc(item.ChunkType)}: {item.Count}")));
    }

    private void AddSection(string title)
    {
        _sections.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#B8C6D8")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(0, 5, 0, 5),
            Child = new TextBlock
            {
                Text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title),
                FontSize = 16,
                FontWeight = FontWeight.Bold
            }
        });
    }

    private void AddValue(string label, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            ColumnSpacing = 12,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap }
            }
        };
        Grid.SetColumn((Control)row.Children[1], 1);
        _sections.Children.Add(row);
    }

    private static void AddTableCell(Grid grid, int row, int column, string text, bool header)
    {
        if (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        var cell = new Border
        {
            Padding = new Avalonia.Thickness(6),
            Background = new SolidColorBrush(Color.Parse(header ? "#E8EDF5" : "#F8F9FB")),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = header ? FontWeight.Bold : FontWeight.Normal,
                TextWrapping = TextWrapping.Wrap
            }
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static string FormatAddress(ushort address) => $"0x{address:X4}";

    private static string FormatOptionalAddress(ushort? address) => address is { } value ? FormatAddress(value) : "Not present";

    private static string DisplayFourCc(string usage) => usage.EndsWith(' ')
        ? $"'{usage}'"
        : usage;
}
