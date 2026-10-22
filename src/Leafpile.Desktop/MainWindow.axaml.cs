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
    private bool _inputActive;
    private bool _cursorVisible;
    private int _maximumInputCharacters;
    private string _inputText = string.Empty;
    private string _lowerWindowText = string.Empty;
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _auditRefreshTimer;
    private readonly OpenStoryDirectoryStore _openStoryDirectoryStore;
    private readonly InstructionAuditLog _instructionAuditLog = new();
    private InstructionWindow? _instructionWindow;
    private InstructionAuditSession? _auditSession;
    private int _displayedAuditLineCount;
    private string _storyName = string.Empty;
    private int _renderQueued;
    private (int Window, uint Picture, int X, int Y, uint Width, uint Height)[] _pictureKeys = [];

    public MainWindow()
    {
        InitializeComponent();
        _openStoryDirectoryStore = new OpenStoryDirectoryStore(OpenStoryDirectoryStore.GetDefaultPreferenceFilePath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TextInput += OnWindowTextInput;
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            UpdateLowerText();
        };
        _auditRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _auditRefreshTimer.Tick += (_, _) => RefreshInstructionWindow();
        Closing += (_, _) =>
        {
            StopCurrentSession("application closed");
            _auditRefreshTimer.Stop();
            if (_instructionWindow is not null)
            {
                _instructionWindow.AllowOwnerClose();
                _instructionWindow.Close();
            }
        };
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
            HideInput();
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
        var warnings = new List<string>();
        var preference = _openStoryDirectoryStore.Read();
        if (preference.Warning is not null) warnings.Add(preference.Warning);

        IStorageFolder? startLocation = null;
        try
        {
            if (preference.Directory is not null)
            {
                try
                {
                    startLocation = await StorageProvider.TryGetFolderFromPathAsync(new Uri(preference.Directory));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    warnings.Add($"Could not open the previously used story folder; the picker will use its default location. {exception.Message}");
                }

                if (startLocation is null)
                {
                    var cleared = _openStoryDirectoryStore.Clear();
                    if (cleared.Warning is not null)
                    {
                        warnings.Add(cleared.Warning);
                    }
                    else if (warnings.Count == 0)
                    {
                        warnings.Add("The previously used story folder is unavailable; the picker will use its default location.");
                    }
                }
            }

            var files = await StorageProvider.OpenFilePickerAsync(CreateOpenStoryPickerOptions(startLocation));
            if (files.Count == 0)
            {
                ReportOpenStoryWarnings(warnings);
                return;
            }

            var localPath = files[0].TryGetLocalPath();
            if (localPath is not null)
            {
                var remembered = _openStoryDirectoryStore.RememberSelectedStory(localPath);
                if (remembered.Warning is not null) warnings.Add(remembered.Warning);
            }
            else
            {
                warnings.Add("The selected story folder could not be remembered because the file picker did not provide a local path.");
            }

            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            StartStory(StoryLoader.Load(memory.ToArray()), files[0].Name ?? "Story");
            ReportOpenStoryWarnings(warnings);
        }
        catch (Exception exception) when (exception is StoryLoadException or IOException or UnauthorizedAccessException)
        {
            warnings.Add(exception.Message);
            ReportStoryLoadError(string.Join(Environment.NewLine, warnings));
        }
        finally
        {
            startLocation?.Dispose();
        }
    }

    internal static FilePickerOpenOptions CreateOpenStoryPickerOptions(IStorageFolder? startLocation) => new()
    {
        Title = "Open Z-machine story",
        SuggestedStartLocation = startLocation,
        AllowMultiple = false,
        FileTypeFilter = [new FilePickerFileType("Z-machine story or Blorb") { Patterns = ["*.z1", "*.z2", "*.z3", "*.z4", "*.z5", "*.z6", "*.z7", "*.z8", "*.blorb", "*.blb", "*.*"] }]
    };

    private void ReportOpenStoryWarnings(IReadOnlyCollection<string> warnings)
    {
        if (warnings.Count > 0)
        {
            ReportStoryLoadError(string.Join(Environment.NewLine, warnings));
        }
    }

    internal async Task OpenStoryFromCommandLineAsync(string[] args)
    {
        if (args.Length != 1)
        {
            ReportStoryLoadError("Pass exactly one story file path on the command line.");
            return;
        }

        var path = args[0];
        try
        {
            var storyBytes = await File.ReadAllBytesAsync(path);
            StartStory(StoryLoader.Load(storyBytes), Path.GetFileName(path) ?? "Story");
        }
        catch (Exception exception) when (exception is StoryLoadException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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

    private void OnSaveGame(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        SubmitGameCommand("save");
    }

    private void OnRestoreGame(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        SubmitGameCommand("restore");
    }

    private void SubmitGameCommand(string command)
    {
        if (_currentInput is null || !_inputActive || _inputIsCharacter || command.Length > _maximumInputCharacters)
        {
            return;
        }

        _inputText = command;
        UpdateLowerText();
        SubmitInput();
    }

    private void OnQuit(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Close();
    }

    private void OnViewInstructions(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        var created = _instructionWindow is null;
        if (created)
        {
            _instructionWindow = new InstructionWindow();
            _instructionWindow.BindAuditLog(_instructionAuditLog);
            _instructionWindow.Closed += (_, _) => _instructionWindow = null;
            _instructionWindow.HistoryCleared += displayedLines => _displayedAuditLineCount = displayedLines;
        }

        var window = _instructionWindow!;
        _displayedAuditLineCount = window.ClearDisplayed();
        var scaling = Screens.ScreenFromWindow(this)?.Scaling ?? 1;
        var rightOfMainWindow = Position.X + (int)Math.Round(Bounds.Width * scaling);
        var screen = Screens.ScreenFromWindow(this);
        var windowWidthPixels = (int)Math.Round(window.Width * scaling);
        var windowHeightPixels = (int)Math.Round(Bounds.Height * scaling);
        var top = Position.Y;
        if (screen is not null)
        {
            if (rightOfMainWindow + windowWidthPixels > screen.WorkingArea.Right)
            {
                var leftOfMainWindow = Position.X - windowWidthPixels;
                rightOfMainWindow = leftOfMainWindow >= screen.WorkingArea.X ? leftOfMainWindow : screen.WorkingArea.X;
            }
            top = Math.Clamp(top, screen.WorkingArea.Y, Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - windowHeightPixels));
        }
        window.Position = new PixelPoint(rightOfMainWindow, top);
        window.Height = Bounds.Height;
        if (window.IsVisible) window.Activate();
        else if (created) window.Show(this);
        else window.Show();
        StorySurface.Focus();
        _auditRefreshTimer.Start();
    }

    private void RefreshInstructionWindow()
    {
        if (_instructionWindow is null || !_instructionWindow.IsVisible) return;
        if (_displayedAuditLineCount > _instructionAuditLog.Count) _displayedAuditLineCount = 0;
        var lines = _instructionAuditLog.SnapshotFrom(_displayedAuditLineCount);
        if (lines.Length == 0) return;
        _instructionWindow.Append(lines);
        _displayedAuditLineCount += lines.Length;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (_currentInput is not null)
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                SubmitInput();
                return;
            }

            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                if (_auditSession is not null) _instructionAuditLog.BeginCommand(_auditSession, "[cancel input]");
                _inputOutput?.CancelPendingInput();
                _currentInput.TrySetResult(new ZMachineInputResponse(string.Empty, 13));
                _currentInput = null;
                HideInput();
                return;
            }

            if (!_inputIsCharacter && args.Key == Key.Back)
            {
                args.Handled = true;
                if (_inputText.Length > 0)
                {
                    _inputText = _inputText[..^1];
                    UpdateLowerText();
                }

                return;
            }
        }

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

    private void OnWindowTextInput(object? sender, TextInputEventArgs args)
    {
        _ = sender;
        if (_currentInput is null || string.IsNullOrEmpty(args.Text)) return;
        if (_inputIsCharacter)
        {
            var character = args.Text[0];
            var code = character <= byte.MaxValue ? (byte)character : (byte)'?';
            if (_auditSession is not null) _instructionAuditLog.BeginCommand(_auditSession, args.Text);
            _currentInput.TrySetResult(new ZMachineInputResponse(string.Empty, code));
            _currentInput = null;
            HideInput();
            args.Handled = true;
            return;
        }

        foreach (var character in args.Text)
        {
            if (!char.IsControl(character) && _inputText.Length < _maximumInputCharacters)
                _inputText += character;
        }

        UpdateLowerText();
        args.Handled = true;
    }

    private void StartStory(LoadedStory story, string name)
    {
        StopCurrentSession("story restarted or replaced");
        _story = story;
        _storyName = name;
        var auditSession = _instructionAuditLog.StartRun(name);
        _auditSession = auditSession;
        Title = $"LeafpileGPT — {name}";
        RestartMenuItem.IsEnabled = true;
        WelcomeText.IsVisible = false;
        RuntimeErrorText.IsVisible = false;
        StatusBar.IsVisible = story.Header.Version <= 3;
        HideInput();
        _lowerWindowText = string.Empty;
        LowerText.Text = string.Empty;
        PictureCanvas.Children.Clear();
        _pictureKeys = [];
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        var state = new ZMachineState(story);
        ConfigureScreenDimensions(state.Screen);
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
            display: this,
            successfullyExecutedInstructionObserver: instruction => _instructionAuditLog.Record(auditSession, instruction));
        Present(state.Screen);
        _ = Task.Run(() => RunStory(state, executor, input, auditSession, cancellation.Token), cancellation.Token);
    }

    private void ConfigureScreenDimensions(ZMachineScreen screen)
    {
        var bounds = StorySurface.Bounds;
        var availableWidth = bounds.Width - LowerText.Margin.Left - LowerText.Margin.Right;
        var availableHeight = bounds.Height - LowerText.Margin.Top - LowerText.Margin.Bottom;
        if (availableWidth <= 0 || availableHeight <= 0) return;

        // Courier New is a fixed-pitch font with glyphs that are 0.6 em wide and 1.2 em high.
        var characterWidth = LowerText.FontSize * 0.6;
        var lineHeight = LowerText.FontSize * 1.2;
        var columns = Math.Clamp((int)Math.Floor(availableWidth / characterWidth), 1, byte.MaxValue);
        var rows = Math.Clamp((int)Math.Floor(availableHeight / lineHeight), 1, byte.MaxValue);
        var widthUnits = screen.Version <= 4 ? columns : Math.Clamp((int)Math.Round(availableWidth), 1, ushort.MaxValue);
        var heightUnits = screen.Version <= 4 ? rows : Math.Clamp((int)Math.Round(availableHeight), 1, ushort.MaxValue);
        screen.SetDimensions(columns, rows, widthUnits, heightUnits);
    }

    private void StopCurrentSession(string reason)
    {
        if (_sessionCancellation is not null && _auditSession is not null)
            _instructionAuditLog.MarkRunEnded(_auditSession, reason);
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _inputOutput?.CancelPendingInput();
        _currentInput?.TrySetResult(new ZMachineInputResponse(string.Empty, 13));
        _currentInput = null;
        HideInput();
    }

    private void RunStory(ZMachineState state, ZMachineOpcodeExecutor executor, DesktopInputOutputHost input, InstructionAuditSession auditSession, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = executor.ExecuteNext();
                if (result.Halted)
                {
                    _instructionAuditLog.MarkRunEnded(auditSession, "story halted");
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            _instructionAuditLog.MarkRunEnded(auditSession, cancellationToken.IsCancellationRequested ? "session stopped" : $"runtime error: {exception.Message}");
            input.CancelPendingInput();
        }
    }

    private void ShowInputRequest(ZMachineInputRequest request, TaskCompletionSource<ZMachineInputResponse> completion)
    {
        RuntimeErrorText.IsVisible = false;
        _currentInput = completion;
        _inputIsCharacter = request.ReadCharacter;
        _inputText = request.InitialText;
        _maximumInputCharacters = request.MaximumCharacters > 0 ? request.MaximumCharacters : int.MaxValue;
        _inputActive = true;
        UpdateFileCommandMenu();
        _cursorVisible = true;
        _cursorTimer.Start();
        UpdateLowerText();
        StorySurface.Focus();
    }

    private void SubmitInput()
    {
        if (_inputIsCharacter) return;
        var completion = _currentInput;
        if (completion is null || completion.Task.IsCompleted) return;
        if (_auditSession is not null) _instructionAuditLog.BeginCommand(_auditSession, _inputText);
        if (!completion.TrySetResult(new ZMachineInputResponse(_inputText, 13))) return;
        _currentInput = null;
        HideInput();
    }

    private void RenderScreen(ZMachineScreen screen)
    {
        StatusBar.IsVisible = screen.Version <= 3;
        StatusLocationText.Text = screen.StatusLocation;
        StatusValuesText.Text = screen.StatusValues;
        UpperText.Text = screen.UpperWindowText;
        UpperText.IsVisible = !string.IsNullOrEmpty(screen.UpperWindowText);
        _lowerWindowText = screen.LowerWindowText;
        UpdateLowerText();
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
        RenderPictures(screen);
    }

    private void HideInput()
    {
        _inputActive = false;
        UpdateFileCommandMenu();
        _inputText = string.Empty;
        _cursorTimer.Stop();
        UpdateLowerText();
    }

    private void UpdateLowerText()
    {
        LowerText.Text = _inputActive
            ? string.Concat(_lowerWindowText, _inputText, _cursorVisible ? "█" : " ")
            : _lowerWindowText;
    }

    private void UpdateFileCommandMenu()
    {
        SaveMenuItem.IsEnabled = _story is not null && _inputActive && !_inputIsCharacter && _maximumInputCharacters >= "save".Length;
        RestoreMenuItem.IsEnabled = _story is not null && _inputActive && !_inputIsCharacter && _maximumInputCharacters >= "restore".Length;
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
