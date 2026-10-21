using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.Desktop;

public sealed partial class MainWindow : Window, IZMachineDisplay
{
    private LoadedStory? _story;
    private CancellationTokenSource? _sessionCancellation;
    private DesktopInputOutputHost? _inputOutput;
    private TaskCompletionSource<ZMachineInputResponse>? _currentInput;
    private bool _inputIsCharacter;
    private string _storyName = string.Empty;
    private int _renderQueued;
    private (int Window, uint Picture, int X, int Y, uint Width, uint Height)[] _pictureKeys = [];

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += (_, _) => StopCurrentSession();
    }

    public void Present(ZMachineScreen screen)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RenderScreen(screen);
            return;
        }

        if (Interlocked.Exchange(ref _renderQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _renderQueued, 0);
            RenderScreen(screen);
        });
    }

    public void ReportRuntimeError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RuntimeErrorText.Text = message;
            RuntimeErrorText.IsVisible = true;
            InputBox.IsEnabled = false;
            InputBox.IsVisible = false;
            PromptText.IsVisible = false;
        });
    }

    private void ReportStoryLoadError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RuntimeErrorText.Text = message;
            RuntimeErrorText.IsVisible = true;
        });
    }

    private async void OnOpenStory(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Z-machine story",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Z-machine story or Blorb") { Patterns = ["*.z1", "*.z2", "*.z3", "*.z4", "*.z5", "*.z6", "*.z7", "*.z8", "*.blorb", "*.blb", "*.*"] }]
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            StartStory(StoryLoader.Load(memory.ToArray()), files[0].Name ?? "Story");
        }
        catch (Exception exception) when (exception is StoryLoadException or IOException or UnauthorizedAccessException)
        {
            ReportStoryLoadError(exception.Message);
        }
    }

    private void OnRestart(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (_story is not null) StartStory(_story, _storyName);
    }

    private void OnQuit(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;
        if (args.Key == Key.O)
        {
            args.Handled = true;
            OnOpenStory(this, new Avalonia.Interactivity.RoutedEventArgs());
        }
        else if (args.Key == Key.R && _story is not null)
        {
            args.Handled = true;
            StartStory(_story, _storyName);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            SubmitInput();
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            _inputOutput?.CancelPendingInput();
            _currentInput?.TrySetResult(new ZMachineInputResponse(string.Empty, 13));
            _currentInput = null;
            InputBox.IsVisible = false;
            PromptText.IsVisible = false;
        }
    }

    private void OnInputTextInput(object? sender, TextInputEventArgs args)
    {
        _ = sender;
        if (!_inputIsCharacter || string.IsNullOrEmpty(args.Text)) return;
        var character = args.Text[0];
        var code = character <= byte.MaxValue ? (byte)character : (byte)'?';
        _currentInput?.TrySetResult(new ZMachineInputResponse(string.Empty, code));
        _currentInput = null;
        InputBox.Text = string.Empty;
        InputBox.IsVisible = false;
        PromptText.IsVisible = false;
        args.Handled = true;
    }

    private void StartStory(LoadedStory story, string name)
    {
        StopCurrentSession();
        _story = story;
        _storyName = name;
        Title = $"LeafpileGPT — {name}";
        RestartMenuItem.IsEnabled = true;
        WelcomeText.IsVisible = false;
        RuntimeErrorText.IsVisible = false;
        StatusBar.IsVisible = story.Header.Version <= 3;
        InputBox.Text = string.Empty;
        InputBox.IsVisible = false;
        PictureCanvas.Children.Clear();
        _pictureKeys = [];
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        var state = new ZMachineState(story);
        var input = new DesktopInputOutputHost((request, completion) =>
        {
            Present(state.Screen);
            ShowInputRequest(request, completion);
        });
        _inputOutput = input;
        var saveFiles = new DesktopSaveFileAccess(this);
        var executor = new ZMachineOpcodeExecutor(
            state,
            new QuetzalSaveRestoreService(saveFiles),
            inputOutput: input,
            sound: new DesktopSoundHost(),
            display: this);
        Present(state.Screen);
        _ = Task.Run(() => RunStory(state, executor, input, cancellation.Token), cancellation.Token);
    }

    private void StopCurrentSession()
    {
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _inputOutput?.CancelPendingInput();
        _currentInput?.TrySetResult(new ZMachineInputResponse(string.Empty, 13));
        _currentInput = null;
    }

    private void RunStory(ZMachineState state, ZMachineOpcodeExecutor executor, DesktopInputOutputHost input, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = executor.ExecuteNext();
                if (result.Halted) break;
            }
        }
        catch (Exception)
        {
            input.CancelPendingInput();
        }
    }

    private void ShowInputRequest(ZMachineInputRequest request, TaskCompletionSource<ZMachineInputResponse> completion)
    {
        RuntimeErrorText.IsVisible = false;
        _currentInput = completion;
        _inputIsCharacter = request.ReadCharacter;
        InputBox.Text = request.InitialText;
        InputBox.MaxLength = request.MaximumCharacters > 0 ? request.MaximumCharacters : int.MaxValue;
        InputBox.IsEnabled = true;
        InputBox.IsVisible = true;
        PromptText.IsVisible = !request.ReadCharacter;
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
    }

    private void SubmitInput()
    {
        if (_inputIsCharacter) return;
        var completion = _currentInput;
        if (completion is null || !completion.TrySetResult(new ZMachineInputResponse(InputBox.Text ?? string.Empty, 13))) return;
        _currentInput = null;
        InputBox.IsVisible = false;
        PromptText.IsVisible = false;
        InputBox.Text = string.Empty;
    }

    private void RenderScreen(ZMachineScreen screen)
    {
        StatusBar.IsVisible = screen.Version <= 3;
        StatusLocationText.Text = screen.StatusLocation;
        StatusValuesText.Text = screen.StatusValues;
        UpperText.Text = screen.UpperWindowText;
        UpperText.IsVisible = !string.IsNullOrEmpty(screen.UpperWindowText);
        LowerText.Text = screen.LowerWindowText;
        var foreground = screen.Version < 5
            ? Avalonia.Media.Colors.White
            : screen.Version == 6
            ? ScreenColour(screen.GetWindowProperty(checked((ushort)screen.SelectedWindow), 16), foreground: true)
            : ScreenColour(unchecked((ushort)screen.Foreground), foreground: true);
        var background = screen.Version < 5
            ? Avalonia.Media.Color.Parse("#0066CC")
            : screen.Version == 6
            ? ScreenColour(screen.GetWindowProperty(checked((ushort)screen.SelectedWindow), 17), foreground: false)
            : ScreenColour(unchecked((ushort)screen.Background), foreground: false);
        UpperText.Foreground = LowerText.Foreground = new Avalonia.Media.SolidColorBrush(foreground);
        UpperText.Background = LowerText.Background = new Avalonia.Media.SolidColorBrush(background);
        StorySurface.Background = new Avalonia.Media.SolidColorBrush(background);
        InputBox.Foreground = new Avalonia.Media.SolidColorBrush(foreground);
        InputBox.Background = new Avalonia.Media.SolidColorBrush(background);
        InputBox.CaretBrush = new Avalonia.Media.SolidColorBrush(foreground);
        RenderPictures(screen);
    }

    private static Avalonia.Media.Color ScreenColour(ushort value, bool foreground)
    {
        if (value is >= 2 and <= 12)
        {
            var colours = new[] { "#000000", "#CC0000", "#00AA00", "#AA5500", "#0000CC", "#AA00AA", "#00AAAA", "#FFFFFF", "#AAAAAA", "#FF5555", "#55FF55" };
            return Avalonia.Media.Color.Parse(colours[value - 2]);
        }

        if (value <= 0x7fff)
        {
            var red = (byte)(((value >> 10) & 0x1f) * 255 / 31);
            var green = (byte)(((value >> 5) & 0x1f) * 255 / 31);
            var blue = (byte)((value & 0x1f) * 255 / 31);
            return Avalonia.Media.Color.FromRgb(red, green, blue);
        }

        return foreground ? Avalonia.Media.Colors.White : Avalonia.Media.Color.Parse("#0066CC");
    }

    private void RenderPictures(ZMachineScreen screen)
    {
        var operations = screen.DisplayedPictures;
        var keys = operations.Select(operation => (
            operation.Window,
            operation.Picture.Number,
            operation.X,
            operation.Y,
            operation.DisplaySize.Width,
            operation.DisplaySize.Height)).ToArray();
        if (_pictureKeys.SequenceEqual(keys)) return;
        _pictureKeys = keys;
        PictureCanvas.Children.Clear();
        foreach (var operation in operations)
        {
            if (operation.Picture.Format == BlorbPictureFormat.Rectangle) continue;
            try
            {
                using var stream = new MemoryStream(GetPictureImageBytes(operation), writable: false);
                var image = new Image
                {
                    Source = new Bitmap(stream),
                    Width = operation.DisplaySize.Width,
                    Height = operation.DisplaySize.Height,
                    Stretch = Avalonia.Media.Stretch.Fill
                };
                Canvas.SetLeft(image, operation.X);
                Canvas.SetTop(image, operation.Y);
                PictureCanvas.Children.Add(image);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                RuntimeErrorText.Text = $"Picture {operation.Picture.Number} could not be rendered: {exception.Message}";
                RuntimeErrorText.IsVisible = true;
            }
        }
    }

    private static byte[] GetPictureImageBytes(ZMachinePictureOperation operation)
    {
        var data = operation.Picture.Data.ToArray();
        if (!operation.Picture.UsesAdaptivePalette || operation.PaletteOverride.Count == 0) return data;

        var offset = 8;
        while (offset <= data.Length - 12)
        {
            var encodedLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            var chunkDataOffset = offset + 8;
            if (encodedLength > int.MaxValue) throw new InvalidDataException("The PNG chunk length exceeds the supported range.");
            var chunkLength = (int)encodedLength;
            if (chunkLength > data.Length - chunkDataOffset - 4) throw new InvalidDataException("The PNG palette chunk exceeds the picture resource.");
            if (data.AsSpan(offset + 4, 4).SequenceEqual("PLTE"u8))
            {
                if (chunkLength % 3 != 0) throw new InvalidDataException("The PNG palette chunk has an invalid length.");
                var colors = Math.Min(chunkLength / 3, operation.PaletteOverride.Count);
                for (var index = 0; index < colors; index++)
                {
                    var paletteColor = operation.PaletteOverride[index];
                    var colorOffset = chunkDataOffset + index * 3;
                    data[colorOffset] = paletteColor.Red;
                    data[colorOffset + 1] = paletteColor.Green;
                    data[colorOffset + 2] = paletteColor.Blue;
                }

                var crc = PngCrc32(data.AsSpan(offset + 4, chunkLength + 4));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(chunkDataOffset + chunkLength, 4), crc);
                return data;
            }

            offset = checked(chunkDataOffset + chunkLength + 4);
        }

        throw new InvalidDataException("An adaptive-palette PNG has no PLTE chunk.");
    }

    private static uint PngCrc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = unchecked((uint)-(int)(crc & 1));
                crc = (crc >> 1) ^ (0xedb88320u & mask);
            }
        }

        return ~crc;
    }
}
