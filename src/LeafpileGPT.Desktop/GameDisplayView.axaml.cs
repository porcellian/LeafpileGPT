using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public partial class GameDisplayView : UserControl
{
    private readonly ScrollViewer _transcriptScrollViewer;
    private readonly Grid _playArea;
    private readonly Border _upperWindowBorder;
    private readonly Canvas _pictureCanvas;
    private readonly TextBlock _transcriptTextBlock;
    private readonly StackPanel _upperWindowTextPanel;
    private readonly Canvas _upperWindowBackgroundCanvas;
    private readonly Canvas _upperWindowInputCanvas;
    private readonly TextBox _promptTextBox;
    private TextBlock? _upperWindowPromptBlock;
    private TextBlock? _promptInputHost;
    private Run? _promptInputRun;
    private Run? _blockCursorRun;
    private bool _promptInputActive;
    private bool _scrollToEndPending;
    private int _storyVersion;
    private DesktopScreenMetrics? _lastScreenMetrics;
    private readonly DispatcherTimer _cursorBlinkTimer;
    private readonly Dictionary<(short Window, int Row, int Column), Image> _pictures = [];

    public GameDisplayView()
    {
        AvaloniaXamlLoader.Load(this);
        _transcriptScrollViewer = this.FindControl<ScrollViewer>("TranscriptScrollViewer")!;
        _playArea = this.FindControl<Grid>("PlayArea")!;
        _upperWindowBorder = this.FindControl<Border>("UpperWindowBorder")!;
        _pictureCanvas = this.FindControl<Canvas>("PictureCanvas")!;
        _transcriptTextBlock = this.FindControl<TextBlock>("TranscriptTextBlock")!;
        _upperWindowTextPanel = this.FindControl<StackPanel>("UpperWindowTextPanel")!;
        _upperWindowBackgroundCanvas = this.FindControl<Canvas>("UpperWindowBackgroundCanvas")!;
        _upperWindowInputCanvas = this.FindControl<Canvas>("UpperWindowInputCanvas")!;
        _transcriptTextBlock.LayoutUpdated += OnTranscriptLayoutUpdated;
        _promptTextBox = this.FindControl<TextBox>("PromptTextBox")!;
        _promptTextBox.TextInput += OnPromptTextInput;
        _playArea.SizeChanged += (_, _) => PublishScreenMetrics();
        _transcriptScrollViewer.SizeChanged += (_, _) => PublishScreenMetrics();
        Loaded += (_, _) => PublishScreenMetrics();
        _cursorBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cursorBlinkTimer.Tick += (_, _) =>
        {
            if (_blockCursorRun is not null)
            {
                _blockCursorRun.Foreground = _blockCursorRun.Foreground == Brushes.White
                    ? Brushes.Transparent
                    : Brushes.White;
            }
        };
        _promptTextBox.GotFocus += (_, _) =>
        {
            if (_blockCursorRun is not null)
            {
                _blockCursorRun.Foreground = Brushes.White;
            }

            _cursorBlinkTimer.Start();
        };
        _promptTextBox.LostFocus += (_, _) =>
        {
            _cursorBlinkTimer.Stop();
            if (_blockCursorRun is not null)
            {
                _blockCursorRun.Foreground = Brushes.Transparent;
            }
        };
        Unloaded += (_, _) => _cursorBlinkTimer.Stop();
        Model = new GameDisplayModel();
        DataContext = Model;
        Model.OutputSegmentAdded += AppendOutputSegment;
        Model.UpperWindowSegmentsChanged += RefreshUpperWindowSegments;
        Model.OutputCleared += OnOutputCleared;
        Model.PresentationReset += EndPromptInput;
        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GameDisplayModel.InputText))
            {
                if (_promptInputRun is not null)
                {
                    _promptInputRun.Text = Model.InputText;
                    if (_promptInputHost == _transcriptTextBlock)
                    {
                        RequestScrollToEnd();
                    }
                }
            }
            else if (args.PropertyName == nameof(GameDisplayModel.Transcript))
            {
                RequestScrollToEnd();
            }
        };
    }

    public GameDisplayModel Model { get; }

    public event Action? MoreKeySubmitted;

    public event Action<DesktopScreenMetrics>? ScreenMetricsChanged;

    public void FocusPrompt() => _promptTextBox.Focus();

    public void SetStoryVersion(int version)
    {
        _storyVersion = version;
        Model.SetStoryVersion(version);
        _upperWindowBorder.Background = new SolidColorBrush(Color.FromRgb(0, 102, 204));
    }

    public bool TryGetScreenMetrics(out DesktopScreenMetrics metrics)
    {
        var typeface = new Typeface(
            _transcriptTextBlock.FontFamily,
            _transcriptTextBlock.FontStyle,
            _transcriptTextBlock.FontWeight,
            FontStretch.Normal);
        var sample = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            _transcriptTextBlock.FontSize,
            Brushes.White);
        // The transcript viewer occupies only the lower window after a story
        // splits the screen. Measure the play area instead so upper-window
        // height never gets subtracted from the VM's total screen dimensions.
        var contentWidth = _playArea.Bounds.Width;
        var contentHeight = _playArea.Bounds.Height;
        if (contentWidth <= 0 || contentHeight <= 0 || sample.WidthIncludingTrailingWhitespace <= 0 || sample.Height <= 0)
        {
            metrics = default;
            return false;
        }

        metrics = DesktopScreenMetrics.FromViewport(
            contentWidth,
            contentHeight,
            sample.WidthIncludingTrailingWhitespace,
            sample.Height);
        Model.SetUpperWindowWidth(metrics.WidthInCharacters);
        return true;
    }

    private void PublishScreenMetrics()
    {
        if (!TryGetScreenMetrics(out var metrics) || _lastScreenMetrics == metrics)
        {
            return;
        }

        _lastScreenMetrics = metrics;
        ScreenMetricsChanged?.Invoke(metrics);
    }

    private void UpdateUpperWindowLayout()
    {
        if (Model.UpperWindowHeight == 0 || _storyVersion >= 6)
        {
            _upperWindowBorder.Height = double.NaN;
            return;
        }

        var typeface = new Typeface(
            _transcriptTextBlock.FontFamily,
            _transcriptTextBlock.FontStyle,
            _transcriptTextBlock.FontWeight,
            FontStretch.Normal);
        var sample = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            _transcriptTextBlock.FontSize,
            Brushes.White);
        _upperWindowBorder.Height = sample.Height * Model.UpperWindowHeight;
        RefreshUpperWindowBackground();
    }

    public void BeginPromptInput()
    {
        EndPromptInput();
        _promptInputActive = true;
        _promptInputHost = CreatePromptInputHost();
        AddPromptInputRuns();
    }

    public void BeginCharacterInput()
    {
        BeginPromptInput();
        Model.BeginCharacterInput();
        FocusPrompt();
    }

    private void AddPromptInputRuns()
    {
        _promptInputHost ??= _transcriptTextBlock;
        _promptInputRun = new Run(Model.InputText) { Foreground = Brushes.White };
        _blockCursorRun = new Run("█") { Foreground = Brushes.White };
        _promptInputHost.Inlines?.Add(_promptInputRun);
        _promptInputHost.Inlines?.Add(_blockCursorRun);
        _cursorBlinkTimer.Start();
        if (_promptInputHost == _transcriptTextBlock)
        {
            RequestScrollToEnd();
        }
    }

    private TextBlock CreatePromptInputHost()
    {
        if (Model.SelectedWindow != 1 || _storyVersion is < 4 or > 5 || !Model.IsUpperWindowVisible)
        {
            return _transcriptTextBlock;
        }

        var cursor = Model.UpperWindowCursor;
        var host = new TextBlock
        {
            FontFamily = new FontFamily("Courier New, Courier, monospace"),
            FontSize = _transcriptTextBlock.FontSize,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.NoWrap
        };
        _upperWindowInputCanvas.Children.Add(host);
        var (characterWidth, lineHeight) = GetCellMetrics();
        Canvas.SetLeft(host, Math.Max(0, cursor.Column - 1) * characterWidth);
        Canvas.SetTop(host, Math.Max(0, cursor.Row - 1) * lineHeight);
        _upperWindowPromptBlock = host;
        return host;
    }

    private (double CharacterWidth, double LineHeight) GetCellMetrics()
    {
        var typeface = new Typeface(
            _transcriptTextBlock.FontFamily,
            _transcriptTextBlock.FontStyle,
            _transcriptTextBlock.FontWeight,
            FontStretch.Normal);
        var sample = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            _transcriptTextBlock.FontSize,
            Brushes.White);
        return (sample.WidthIncludingTrailingWhitespace, sample.Height);
    }

    public void ApplyScreenOperation(ZMachineScreenOperation operation)
    {
        switch (operation.Kind)
        {
            case ZMachineScreenOperationKind.ClearScreen:
                if (operation.Value == -2)
                {
                    Model.ClearWindow(Model.SelectedWindow);
                }
                else
                {
                    Model.ClearOutput();
                    ClearPictures();
                }

                break;
            case ZMachineScreenOperationKind.ClearWindow:
                Model.ClearWindow(operation.Window);
                ClearPictures(operation.Window);
                break;
            case ZMachineScreenOperationKind.SplitWindow:
                Model.SplitWindow(operation.Value);
                UpdateUpperWindowLayout();
                break;
            case ZMachineScreenOperationKind.SelectWindow:
                Model.SetSelectedWindow(operation.Window);
                break;
            case ZMachineScreenOperationKind.MoveCursor:
                Model.MoveCursor(operation.Window, operation.Row, operation.Column);
                break;
            case ZMachineScreenOperationKind.EraseLine:
                Model.EraseLine(operation.Window, operation.Row, operation.Column);
                break;
            case ZMachineScreenOperationKind.SetTextStyle:
                Model.SetTextStyle(checked((ushort)operation.Value));
                break;
            case ZMachineScreenOperationKind.SetFont:
                Model.SetFont(checked((ushort)operation.Value));
                break;
            case ZMachineScreenOperationKind.SetColours:
                Model.SetColours(checked((ushort)operation.Value));
                break;
            case ZMachineScreenOperationKind.SetTrueColours:
                Model.SetTrueColours(checked((ushort)operation.Value), checked((ushort)operation.SecondaryValue));
                break;
            case ZMachineScreenOperationKind.MorePrompt:
                Model.ShowMorePrompt(visible: true);
                FocusPrompt();
                break;
            case ZMachineScreenOperationKind.MorePromptCleared:
                Model.ShowMorePrompt(visible: false);
                break;
            case ZMachineScreenOperationKind.DrawPicture:
                DrawPicture(operation);
                break;
            case ZMachineScreenOperationKind.ErasePicture:
                ErasePicture(operation);
                break;
            case ZMachineScreenOperationKind.FlushDisplay:
            case ZMachineScreenOperationKind.FlushTextBuffer:
                RequestScrollToEnd();
                break;
        }
    }

    public void ClearPictures()
    {
        ClearPictures(window: null);
    }

    public void ResetAfterRestore() => Model.ResetAfterRestore();

    private void ClearPictures(short? window)
    {
        foreach (var (key, picture) in _pictures.Where(entry => window is null || entry.Key.Window == window).ToArray())
        {
            _pictures.Remove(key);
            _pictureCanvas.Children.Remove(picture);
            (picture.Source as Bitmap)?.Dispose();
        }
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model.IsMorePromptVisible)
        {
            MoreKeySubmitted?.Invoke();
            e.Handled = true;
            return;
        }

        if (Model.IsCharacterInputPending && e.Key == Key.Enter)
        {
            CompleteCharacterInput(string.IsNullOrEmpty(Model.InputText) ? "\r" : Model.InputText[..1]);
            e.Handled = true;
            return;
        }

        if (Model.IsCharacterInputPending && e.Key is Key.Back or Key.Delete)
        {
            Model.InputText = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Model.SubmitCommand();
            EndPromptInput();
            e.Handled = true;
        }
    }

    private void OnPromptTextInput(object? sender, TextInputEventArgs e)
    {
        if (!Model.IsCharacterInputPending || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        e.Handled = true;
        if (Model.InputText.Length == 0)
        {
            Model.InputText = e.Text[..1];
        }
    }

    private void CompleteCharacterInput(string character)
    {
        EndPromptInput();
        Model.InputText = string.Empty;
        Model.CompleteCharacterInput(character);
    }

    private void OnOutputCleared()
    {
        var restorePrompt = _promptInputActive;
        EndPromptInput();
        _transcriptTextBlock.Inlines?.Clear();
        if (restorePrompt)
        {
            _promptInputActive = true;
            _promptInputHost = CreatePromptInputHost();
            AddPromptInputRuns();
        }
    }

    private void RequestScrollToEnd() => _scrollToEndPending = true;

    private void OnTranscriptLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_scrollToEndPending)
        {
            return;
        }

        _scrollToEndPending = false;
        _transcriptScrollViewer.ScrollToEnd();
    }

    private void EndPromptInput()
    {
        _promptInputActive = false;
        _cursorBlinkTimer.Stop();
        _promptInputHost ??= _transcriptTextBlock;
        if (_promptInputRun is not null)
        {
            _promptInputHost.Inlines?.Remove(_promptInputRun);
            _promptInputRun = null;
        }

        if (_blockCursorRun is not null)
        {
            _promptInputHost.Inlines?.Remove(_blockCursorRun);
            _blockCursorRun = null;
        }

        if (_upperWindowPromptBlock is not null)
        {
            _upperWindowInputCanvas.Children.Remove(_upperWindowPromptBlock);
            _upperWindowPromptBlock = null;
        }

        _promptInputHost = null;
    }

    private void DrawPicture(ZMachineScreenOperation operation)
    {
        var picture = operation.Picture;
        if (picture is null || picture.Resource.Format == BlorbPictureFormat.Rectangle)
        {
            return;
        }

        var key = (operation.Window, operation.Row, operation.Column);
        ErasePicture(key);
        using var stream = new MemoryStream(picture.Resource.EncodedData.ToArray(), writable: false);
        var bitmap = new Bitmap(stream);
        var image = new Image
        {
            Source = bitmap,
            Width = picture.DisplayWidth,
            Height = picture.DisplayHeight,
            Stretch = Avalonia.Media.Stretch.Fill
        };
        Canvas.SetLeft(image, operation.Column);
        Canvas.SetTop(image, operation.Row);
        _pictureCanvas.Children.Add(image);
        _pictures.Add(key, image);
    }

    private void ErasePicture(ZMachineScreenOperation operation) =>
        ErasePicture((operation.Window, operation.Row, operation.Column));

    private void ErasePicture((short Window, int Row, int Column) key)
    {
        if (_pictures.Remove(key, out var image))
        {
            _pictureCanvas.Children.Remove(image);
            (image.Source as Bitmap)?.Dispose();
        }
    }

    private void AppendOutputSegment(GameDisplayTextSegment segment)
    {
        _transcriptTextBlock.Inlines?.Add(CreateOutputRun(segment, upperWindow: false));
    }

    private void RefreshUpperWindowSegments()
    {
        RefreshUpperWindowBackground();
        _upperWindowTextPanel.Children.Clear();
        var currentLine = CreateUpperWindowLine();
        _upperWindowTextPanel.Children.Add(currentLine);
        foreach (var segment in Model.UpperWindowSegments)
        {
            if (segment.Text == "\n")
            {
                currentLine = CreateUpperWindowLine();
                _upperWindowTextPanel.Children.Add(currentLine);
                continue;
            }

            currentLine.Inlines?.Add(CreateOutputRun(segment, upperWindow: true));
        }
    }

    private void RefreshUpperWindowBackground()
    {
        _upperWindowBackgroundCanvas.Children.Clear();
        var firstColumn = int.MaxValue;
        var lastColumn = 0;
        var column = 1;

        foreach (var segment in Model.UpperWindowSegments)
        {
            if (segment.Text == "\n")
            {
                column = 1;
                continue;
            }

            var (_, background) = ResolveOutputColours(segment, upperWindow: true);
            var cellCount = segment.Text.EnumerateRunes().Count();
            if (background == Color.FromRgb(170, 170, 170) && cellCount > 0)
            {
                firstColumn = Math.Min(firstColumn, column);
                lastColumn = Math.Max(lastColumn, column + cellCount - 1);
            }

            column += cellCount;
        }

        if (lastColumn == 0)
        {
            return;
        }

        var (characterWidth, lineHeight) = GetCellMetrics();
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
            Width = (lastColumn - firstColumn + 1) * characterWidth,
            Height = lineHeight * Math.Max(1, Model.UpperWindowHeight)
        };
        Canvas.SetLeft(panel, (firstColumn - 1) * characterWidth);
        _upperWindowBackgroundCanvas.Children.Add(panel);
    }

    private TextBlock CreateUpperWindowLine() => new()
    {
        FontFamily = new FontFamily("Courier New, Courier, monospace"),
        FontSize = _transcriptTextBlock.FontSize,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
    };

    private Run CreateOutputRun(GameDisplayTextSegment segment, bool upperWindow)
    {
        var style = segment.Style;
        var (foreground, background) = ResolveOutputColours(segment, upperWindow);

        return new Run(segment.Text)
        {
            FontFamily = new FontFamily("Courier New, Courier, monospace"),
            FontSize = _transcriptTextBlock.FontSize,
            FontWeight = (style & ZMachineTextStyle.Bold) != 0 ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = (style & ZMachineTextStyle.Italic) != 0 ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(foreground),
            Background = new SolidColorBrush(background)
        };
    }

    private (Color Foreground, Color Background) ResolveOutputColours(GameDisplayTextSegment segment, bool upperWindow)
    {
        var foreground = segment.HasExplicitColours
            ? ResolveColour(segment.Foreground, segment.HasTrueColours)
            : Color.FromRgb(255, 255, 255);
        var background = segment.HasExplicitColours
            ? ResolveColour(segment.Background, segment.HasTrueColours)
            : Color.FromRgb(0, 102, 204);
        if ((segment.Style & ZMachineTextStyle.ReverseVideo) != 0)
        {
            if (upperWindow && !segment.HasExplicitColours && _storyVersion is 4 or 5)
            {
                foreground = Color.FromRgb(0, 0, 170);
                background = Color.FromRgb(170, 170, 170);
            }
            else
            {
                (foreground, background) = (background, foreground);
            }
        }

        return (foreground, background);
    }

    private static Color ResolveColour(ushort value, bool trueColour)
    {
        if (trueColour)
        {
            if (value >= 0xFFFC)
            {
                return Colors.Transparent;
            }

            var red = (byte)((value >> 10) & 0x1F);
            var green = (byte)((value >> 5) & 0x1F);
            var blue = (byte)(value & 0x1F);
            return Color.FromRgb((byte)(red * 255 / 31), (byte)(green * 255 / 31), (byte)(blue * 255 / 31));
        }

        return value switch
        {
            2 => Color.FromRgb(0, 0, 0),
            3 => Color.FromRgb(255, 0, 0),
            4 => Color.FromRgb(0, 170, 0),
            5 => Color.FromRgb(255, 255, 0),
            6 => Color.FromRgb(0, 0, 170),
            7 => Color.FromRgb(170, 0, 170),
            8 => Color.FromRgb(0, 170, 170),
            9 => Color.FromRgb(255, 255, 255),
            _ => Color.FromRgb(0, 102, 204)
        };
    }
}
