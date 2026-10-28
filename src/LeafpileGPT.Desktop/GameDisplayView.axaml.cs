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
    private readonly Canvas _pictureCanvas;
    private readonly TextBlock _transcriptTextBlock;
    private readonly TextBlock _promptInputTextBlock;
    private readonly TextBlock _blockCursor;
    private readonly TextBox _promptTextBox;
    private readonly DispatcherTimer _cursorBlinkTimer;
    private readonly Dictionary<(short Window, int Row, int Column), Image> _pictures = [];

    public GameDisplayView()
    {
        AvaloniaXamlLoader.Load(this);
        _transcriptScrollViewer = this.FindControl<ScrollViewer>("TranscriptScrollViewer")!;
        _pictureCanvas = this.FindControl<Canvas>("PictureCanvas")!;
        _transcriptTextBlock = this.FindControl<TextBlock>("TranscriptTextBlock")!;
        _promptInputTextBlock = this.FindControl<TextBlock>("PromptInputTextBlock")!;
        _blockCursor = this.FindControl<TextBlock>("BlockCursor")!;
        _promptTextBox = this.FindControl<TextBox>("PromptTextBox")!;
        _cursorBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cursorBlinkTimer.Tick += (_, _) => _blockCursor.IsVisible = !_blockCursor.IsVisible;
        _promptTextBox.GotFocus += (_, _) =>
        {
            _blockCursor.IsVisible = true;
            _cursorBlinkTimer.Start();
        };
        _promptTextBox.LostFocus += (_, _) =>
        {
            _cursorBlinkTimer.Stop();
            _blockCursor.IsVisible = false;
        };
        Unloaded += (_, _) => _cursorBlinkTimer.Stop();
        Model = new GameDisplayModel();
        DataContext = Model;
        Model.OutputSegmentAdded += AppendOutputSegment;
        Model.OutputCleared += () => _transcriptTextBlock.Inlines?.Clear();
        Model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GameDisplayModel.InputText))
            {
                _promptInputTextBlock.Text = Model.InputText;
            }
            else if (args.PropertyName == nameof(GameDisplayModel.Transcript))
            {
                Dispatcher.UIThread.Post(_transcriptScrollViewer.ScrollToEnd);
            }
        };
    }

    public GameDisplayModel Model { get; }

    public void FocusPrompt() => _promptTextBox.Focus();

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
                break;
            case ZMachineScreenOperationKind.SplitWindow:
                Model.SplitWindow(operation.Value);
                break;
            case ZMachineScreenOperationKind.SelectWindow:
                Model.SetSelectedWindow(operation.Window);
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
                _transcriptScrollViewer.ScrollToEnd();
                break;
        }
    }

    public void ClearPictures()
    {
        foreach (var picture in _pictures.Values)
        {
            (picture.Source as Bitmap)?.Dispose();
        }

        _pictures.Clear();
        _pictureCanvas.Children.Clear();
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Model.SubmitCommand();
            e.Handled = true;
        }
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
        var foreground = segment.HasExplicitColours
            ? ResolveColour(segment.Foreground, segment.HasTrueColours)
            : Color.FromRgb(255, 255, 255);
        var background = segment.HasExplicitColours
            ? ResolveColour(segment.Background, segment.HasTrueColours)
            : Color.FromRgb(0, 102, 204);
        var style = segment.Style;
        if ((style & ZMachineTextStyle.ReverseVideo) != 0)
        {
            (foreground, background) = (background, foreground);
        }

        var run = new Run(segment.Text)
        {
            FontFamily = new FontFamily("Courier New, Courier, monospace"),
            FontWeight = (style & ZMachineTextStyle.Bold) != 0 ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = (style & ZMachineTextStyle.Italic) != 0 ? FontStyle.Italic : FontStyle.Normal,
            Foreground = new SolidColorBrush(foreground),
            Background = new SolidColorBrush(background)
        };
        _transcriptTextBlock.Inlines?.Add(run);
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
