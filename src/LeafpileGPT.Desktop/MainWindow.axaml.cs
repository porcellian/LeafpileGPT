using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public partial class MainWindow : Window
{
    private readonly AppDataPaths _appDataPaths;
    private readonly UserPreferencesStore _preferences;
    private readonly ZMachineRuntimeErrorManager _runtimeErrors;
    private StoryFile? _story;
    private DesktopGameSession? _session;
    private string? _storyPath;
    private TextBlock _appStatusText = null!;
    private GameDisplayView _gameDisplay = null!;
    private MenuItem _saveMenuItem = null!;
    private MenuItem _restoreMenuItem = null!;
    private MenuItem _restartMenuItem = null!;
    private MenuItem _suppressErrorsMenuItem = null!;
    private MenuItem _firstErrorMenuItem = null!;
    private MenuItem _everyErrorMenuItem = null!;
    private MenuItem _stopOnErrorMenuItem = null!;
    private MenuItem _viewInstructionsMenuItem = null!;
    private MenuItem _storyFileInfoMenuItem = null!;
    private RuntimeErrorsWindow? _errorsWindow;
    private InstructionAuditWindow? _instructionsWindow;
    private StoryFileInfoWindow? _storyFileInfoWindow;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _appDataPaths = new AppDataPaths();
        _preferences = new UserPreferencesStore(_appDataPaths);
        _runtimeErrors = new ZMachineRuntimeErrorManager(
            new JsonLineZMachineRuntimeLog(_appDataPaths.LogsDirectory),
            _preferences.Current.RuntimeErrorPolicy);
        _runtimeErrors.DiagnosticAdded += OnRuntimeDiagnosticAdded;
        _appStatusText = this.FindControl<TextBlock>("AppStatusText")!;
        _gameDisplay = this.FindControl<GameDisplayView>("GameDisplay")!;
        _gameDisplay.Model.CommandSubmitted += OnCommandSubmitted;
        _gameDisplay.Model.CharacterSubmitted += OnCommandSubmitted;
        _gameDisplay.MoreKeySubmitted += () => _session?.SubmitMoreKey();
        _gameDisplay.ScreenMetricsChanged += OnScreenMetricsChanged;
        _storyFileInfoMenuItem = this.FindControl<MenuItem>("StoryFileInfoMenuItem")!;
        _viewInstructionsMenuItem = this.FindControl<MenuItem>("ViewInstructionsMenuItem")!;
        _saveMenuItem = this.FindControl<MenuItem>("SaveMenuItem")!;
        _restoreMenuItem = this.FindControl<MenuItem>("RestoreMenuItem")!;
        _restartMenuItem = this.FindControl<MenuItem>("RestartMenuItem")!;
        _suppressErrorsMenuItem = this.FindControl<MenuItem>("SuppressErrorsMenuItem")!;
        _firstErrorMenuItem = this.FindControl<MenuItem>("FirstErrorMenuItem")!;
        _everyErrorMenuItem = this.FindControl<MenuItem>("EveryErrorMenuItem")!;
        _stopOnErrorMenuItem = this.FindControl<MenuItem>("StopOnErrorMenuItem")!;
        UpdateErrorPolicyMenu();
        Closed += OnClosed;
    }

    public async Task LoadStoryFromPathAsync(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var blorb = Path.GetExtension(fullPath).Equals(".blorb", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(fullPath).Equals(".blb", StringComparison.OrdinalIgnoreCase)
                    ? BlorbFile.Load(fullPath)
                    : null;
            var story = blorb?.ZCodeStory ?? (blorb is null ? StoryFile.Load(fullPath) : null);
            if (story is null)
            {
                throw new InvalidDataException("The Blorb container does not include a supported Z-code executable.");
            }

            var session = new DesktopGameSession(
                story,
                fullPath,
                _gameDisplay.Model,
                _gameDisplay.ApplyScreenOperation,
                _appDataPaths.SaveDirectory,
                Path.Combine(_appDataPaths.RootDirectory, "Transcripts"),
                blorb,
                runtimeErrors: _runtimeErrors,
                auditsDirectory: _appDataPaths.AuditsDirectory);
            if (_session is not null)
            {
                await _session.DisposeAsync();
            }

            _story = story;
            _session = session;
            _storyPath = fullPath;
            _storyFileInfoMenuItem.IsEnabled = true;
            _viewInstructionsMenuItem.IsEnabled = true;
            _instructionsWindow?.SetSession(session, fullPath);
            if (_storyFileInfoWindow is { IsVisible: true })
            {
                await RefreshStoryFileInfoAsync(session);
            }
            session.EventServices.SoundPlaybackUnavailable += message => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _appStatusText.Text = message);
            session.InputRequested += _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_session, session))
                {
                    _gameDisplay.BeginPromptInput();
                }
            });
            session.CharacterInputRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_session, session))
                {
                    _gameDisplay.BeginCharacterInput();
                }
            });
            session.TranscriptWriteFailed += exception => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _appStatusText.Text = $"Transcript writing stopped: {exception.Message}");
            UpdateLoadedStoryDisplay();
            _gameDisplay.Model.ResetPresentation();
            _gameDisplay.SetStoryVersion(story.Header.Version);
            _gameDisplay.Model.SetStoryLoaded(loaded: true);
            _gameDisplay.ClearPictures();
            if (story.Header.Version < 6 && _gameDisplay.TryGetScreenMetrics(out var screenMetrics))
            {
                await ApplyScreenMetricsAsync(session, story, screenMetrics);
            }

            _gameDisplay.FocusPrompt();
            _appStatusText.Text = $"Playing {Path.GetFileName(fullPath)} (Z-machine version {story.Header.Version}).";
            RememberSelectedDirectory(FileDialogPurpose.PlayGame, fullPath);
            session.Start();
            _ = ObserveSessionCompletionAsync(session);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or StoryFileFormatException or BlorbFormatException or ZMachineExecutionException)
        {
            _appStatusText.Text = "Could not load the selected story.";
            await ShowErrorAsync("Unable to open story", $"The story could not be loaded from:\n{path}\n\n{exception.Message}");
        }
    }

    private void OnScreenMetricsChanged(DesktopScreenMetrics metrics)
    {
        var session = _session;
        var story = _story;
        if (session is null || story is null || story.Header.Version >= 6)
        {
            return;
        }

        _ = ApplyScreenMetricsAsync(session, story, metrics);
    }

    private static async Task ApplyScreenMetricsAsync(
        DesktopGameSession session,
        StoryFile story,
        DesktopScreenMetrics metrics)
    {
        var (heightInLines, morePageLineLimit) = metrics.ForStoryVersion(story.Header.Version);
        try
        {
            await session.SetScreenDimensionsAsync(
                metrics.WidthInCharacters,
                heightInLines,
                morePageLineLimit);
        }
        catch (ObjectDisposedException)
        {
            // A resize can race with replacing the current story session.
        }
    }

    private async void OnPlayGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var provider = GetStorageProvider();
        if (provider is null)
        {
            await ShowErrorAsync("File picker unavailable", "This window does not have access to a file picker.");
            return;
        }

        try
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Play Game",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Z-machine stories") { Patterns = ["*.z1", "*.z2", "*.z3", "*.z4", "*.z5", "*.z6", "*.z7", "*.z8", "*.blorb", "*.blb"] }, FilePickerFileTypes.All]
            };
            await SetSuggestedStartLocationAsync(provider, options, _preferences.GetLastUsedDirectory(FileDialogPurpose.PlayGame));
            var selected = (await provider.OpenFilePickerAsync(options)).FirstOrDefault();
            if (selected is null)
            {
                return;
            }

            var path = selected.TryGetLocalPath();
            if (path is null)
            {
                await ShowErrorAsync("Unsupported file location", "The selected story is not available as a local file.");
                return;
            }

            RememberSelectedDirectory(FileDialogPurpose.PlayGame, path);
            await LoadStoryFromPathAsync(path);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            await ShowErrorAsync("Unable to select story", exception.Message);
        }
    }

    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_story is null || _session is null)
        {
            return;
        }

        var session = _session;
        var story = _story;
        var provider = GetStorageProvider();
        if (provider is null)
        {
            await ShowErrorAsync("File picker unavailable", "This window does not have access to a file picker.");
            return;
        }

        try
        {
            var saveState = await session.CaptureSaveStateAsync();
            var fileType = new FilePickerFileType("Quetzal save files") { Patterns = ["*.ifzs", "*.sav"] };
            var options = new FilePickerSaveOptions
            {
                Title = "Save Game",
                SuggestedFileName = Path.GetFileNameWithoutExtension(_storyPath) + ".ifzs",
                DefaultExtension = "ifzs",
                FileTypeChoices = [fileType],
                SuggestedFileType = fileType,
                ShowOverwritePrompt = true
            };
            await SetSuggestedStartLocationAsync(provider, options, _preferences.GetLastUsedDirectory(FileDialogPurpose.Save) ?? _appDataPaths.SaveDirectory);
            using var selected = await provider.SaveFilePickerAsync(options);
            if (selected is null)
            {
                return;
            }

            var path = selected.TryGetLocalPath();
            if (path is null)
            {
                await ShowErrorAsync("Unsupported save location", "The selected location cannot be written as a local Quetzal file.");
                return;
            }

            var bytes = QuetzalCodec.Encode(QuetzalCodec.MarkAsHostCheckpoint(saveState), story);
            await File.WriteAllBytesAsync(path, bytes);

            _appStatusText.Text = $"Saved game to {Path.GetFileName(path)}.";
            RememberSelectedDirectory(FileDialogPurpose.Save, path);
        }
        catch (Exception exception) when (IsFileOperationException(exception) || exception is QuetzalFormatException)
        {
            await ShowErrorAsync("Unable to save game", exception.Message);
        }
    }

    private async void OnRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_story is null || _session is null)
        {
            return;
        }

        var session = _session;
        var story = _story;

        var provider = GetStorageProvider();
        if (provider is null)
        {
            await ShowErrorAsync("File picker unavailable", "This window does not have access to a file picker.");
            return;
        }

        try
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Restore Game",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Quetzal save files") { Patterns = ["*.ifzs", "*.sav"] }]
            };
            await SetSuggestedStartLocationAsync(provider, options, _preferences.GetLastUsedDirectory(FileDialogPurpose.Restore) ?? _appDataPaths.SaveDirectory);
            using var selected = (await provider.OpenFilePickerAsync(options)).FirstOrDefault();
            if (selected is null)
            {
                return;
            }

            var path = selected.TryGetLocalPath();
            if (path is null)
            {
                await ShowErrorAsync("Unsupported save location", "The selected save is not available as a local file.");
                return;
            }

            await using var stream = await selected.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var save = QuetzalCodec.Decode(buffer.ToArray(), story);
            await session.ApplySaveStateAsync(save);
            _appStatusText.Text = $"Restored game from {Path.GetFileName(path)}.";
            RememberSelectedDirectory(FileDialogPurpose.Restore, path);
        }
        catch (Exception exception) when (IsFileOperationException(exception) || exception is QuetzalFormatException)
        {
            await ShowErrorAsync("Unable to restore game", exception.Message);
        }
    }

    private async void OnRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            await _session.RestartAsync();
            _appStatusText.Text = "Game restarted.";
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            _appStatusText.Text = "Game restart was cancelled.";
        }
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnViewErrorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_errorsWindow is { IsVisible: true })
        {
            _errorsWindow.Activate();
            return;
        }

        _errorsWindow = new RuntimeErrorsWindow(_runtimeErrors);
        _errorsWindow.Closed += (_, _) => _errorsWindow = null;
        _errorsWindow.Show(this);
    }

    private void OnViewInstructionsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_instructionsWindow is { IsVisible: true })
        {
            _instructionsWindow.SetSession(_session, _storyPath);
            _instructionsWindow.Activate();
            return;
        }

        _instructionsWindow = new InstructionAuditWindow(_appDataPaths.AuditsDirectory);
        _instructionsWindow.SetSession(_session, _storyPath);
        var scale = Screens.ScreenFromWindow(this)?.Scaling ?? 1;
        _instructionsWindow.Position = new Avalonia.PixelPoint(Position.X + (int)Math.Ceiling(Width * scale), Position.Y);
        _instructionsWindow.Closed += (_, _) => _instructionsWindow = null;
        _instructionsWindow.Show(this);
    }

    private async void OnStoryFileInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        await RefreshStoryFileInfoAsync(session);
        if (!ReferenceEquals(_session, session) || _storyFileInfoWindow is not { HasStory: true })
        {
            return;
        }

        if (_storyFileInfoWindow.IsVisible)
        {
            _storyFileInfoWindow.Activate();
            return;
        }

        var scale = Screens.ScreenFromWindow(this)?.Scaling ?? 1;
        _storyFileInfoWindow.Position = new Avalonia.PixelPoint(Position.X + (int)Math.Ceiling(Width * scale), Position.Y);
        var window = _storyFileInfoWindow;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_storyFileInfoWindow, window))
            {
                _storyFileInfoWindow = null;
            }
        };
        window.Show(this);
    }

    private async Task RefreshStoryFileInfoAsync(DesktopGameSession session)
    {
        try
        {
            var snapshot = await session.CaptureStoryFileInfoSnapshotAsync();
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            if (_storyFileInfoWindow is null)
            {
                _storyFileInfoWindow = new StoryFileInfoWindow();
            }

            _storyFileInfoWindow.SetSnapshot(snapshot);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            if (_storyFileInfoWindow is not null)
            {
                _storyFileInfoWindow.SetSnapshot(null);
            }

            _appStatusText.Text = $"Could not refresh story information: {exception.Message}";
        }
    }

    private void OnSuppressErrorsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy.SuppressReports);

    private void OnFirstErrorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType);

    private void OnEveryErrorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy.ReportEveryOccurrence);

    private void OnStopOnErrorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy.StopOnAnyError);

    private void OnCommandSubmitted(string command)
    {
        if (_session is null || !_session.SubmitCommand(command))
        {
            _appStatusText.Text = "The game is not accepting input.";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        _viewInstructionsMenuItem.IsEnabled = false;
        _storyFileInfoMenuItem.IsEnabled = false;
        _instructionsWindow?.SetSession(null, null);
        _storyFileInfoWindow?.SetSnapshot(null);
    }

    private async Task ObserveSessionCompletionAsync(DesktopGameSession session)
    {
        try
        {
            await session.Completion;
            if (ReferenceEquals(_session, session) && session.State.IsHalted && session.TerminalError is null)
            {
                _appStatusText.Text = "The story has ended.";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (ReferenceEquals(_session, session))
            {
                await ShowErrorAsync("Game stopped", exception.Message);
            }
        }
    }

    private void SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy policy)
    {
        _runtimeErrors.SetPolicy(policy);
        try
        {
            _preferences.SetRuntimeErrorPolicy(policy);
            _appStatusText.Text = $"Runtime error policy: {PolicyLabel(policy)}.";
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            _appStatusText.Text = $"Runtime error policy changed for this session; preference could not be saved: {exception.Message}";
        }

        UpdateErrorPolicyMenu();
    }

    private void UpdateErrorPolicyMenu()
    {
        _suppressErrorsMenuItem.Header = PolicyMenuHeader("Suppress reports", ZMachineRuntimeErrorPolicy.SuppressReports);
        _firstErrorMenuItem.Header = PolicyMenuHeader("Report first of each type", ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType);
        _everyErrorMenuItem.Header = PolicyMenuHeader("Report every occurrence", ZMachineRuntimeErrorPolicy.ReportEveryOccurrence);
        _stopOnErrorMenuItem.Header = PolicyMenuHeader("Stop on any error", ZMachineRuntimeErrorPolicy.StopOnAnyError);
    }

    private string PolicyMenuHeader(string label, ZMachineRuntimeErrorPolicy policy) =>
        _runtimeErrors.Policy == policy ? $"{label} ✓" : label;

    private static string PolicyLabel(ZMachineRuntimeErrorPolicy policy) => policy switch
    {
        ZMachineRuntimeErrorPolicy.SuppressReports => "Suppress reports",
        ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType => "Report first of each type",
        ZMachineRuntimeErrorPolicy.ReportEveryOccurrence => "Report every occurrence",
        ZMachineRuntimeErrorPolicy.StopOnAnyError => "Stop on any error",
        _ => policy.ToString()
    };

    private void OnRuntimeDiagnosticAdded(ZMachineRuntimeErrorRecord record) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        switch (RuntimeDiagnosticPresentation.Resolve(record))
        {
            case RuntimeDiagnosticPresentationKind.Hidden:
                return;
            case RuntimeDiagnosticPresentationKind.WarningStatus:
                _appStatusText.Text = $"Runtime warning at 0x{record.ProgramCounter:X6}: {record.Message}";
                return;
            case RuntimeDiagnosticPresentationKind.BlockingDialog:
                _appStatusText.Text = "The game stopped after a runtime error.";
                _ = ShowErrorAsync("Game stopped", record.CopyableDetails);
                return;
        }
    });

    private IStorageProvider? GetStorageProvider() => TopLevel.GetTopLevel(this)?.StorageProvider;

    private static async Task SetSuggestedStartLocationAsync(IStorageProvider provider, PickerOptions options, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(path);
        }
    }

    private void RememberSelectedDirectory(FileDialogPurpose purpose, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                _preferences.RememberDirectory(purpose, directory);
            }
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            _appStatusText.Text += $" (The last folder preference could not be saved: {exception.Message})";
        }
    }

    private void UpdateLoadedStoryDisplay()
    {
        _saveMenuItem.IsEnabled = true;
        _restoreMenuItem.IsEnabled = true;
        _restartMenuItem.IsEnabled = true;
        Title = $"LeafpileGPT — {Path.GetFileName(_storyPath)}";
    }

    private async Task ShowErrorAsync(string title, string details)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBox
                    {
                        Text = details,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MinHeight = 150,
                        MaxHeight = 600
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Copy Details", Name = "CopyErrorDetailsButton" },
                            new Button { Content = "Close", Name = "CloseErrorDialogButton" }
                        }
                    }
                }
            }
        };
        var panel = (StackPanel)dialog.Content;
        var detailBox = (TextBox)panel.Children[0];
        var buttons = (StackPanel)panel.Children[1];
        ((Button)buttons.Children[0]).Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(dialog)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(detailBox.Text ?? string.Empty);
            }
        };
        ((Button)buttons.Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private static bool IsFileOperationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
}
