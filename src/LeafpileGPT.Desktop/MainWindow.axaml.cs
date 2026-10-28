using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public partial class MainWindow : Window
{
    private readonly AppDataPaths _appDataPaths;
    private readonly UserPreferencesStore _preferences;
    private StoryFile? _story;
    private BlorbFile? _blorb;
    private ZMachineExecutionState? _state;
    private string? _storyPath;
    private TextBlock _appStatusText = null!;
    private GameDisplayView _gameDisplay = null!;
    private MenuItem _saveMenuItem = null!;
    private MenuItem _restoreMenuItem = null!;
    private MenuItem _restartMenuItem = null!;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _appDataPaths = new AppDataPaths();
        _preferences = new UserPreferencesStore(_appDataPaths);
        _appStatusText = this.FindControl<TextBlock>("AppStatusText")!;
        _gameDisplay = this.FindControl<GameDisplayView>("GameDisplay")!;
        _gameDisplay.Model.CommandSubmitted += OnCommandSubmitted;
        _saveMenuItem = this.FindControl<MenuItem>("SaveMenuItem")!;
        _restoreMenuItem = this.FindControl<MenuItem>("RestoreMenuItem")!;
        _restartMenuItem = this.FindControl<MenuItem>("RestartMenuItem")!;
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

            var state = ZMachineExecutionState.Start(story);
            _story = story;
            _blorb = blorb;
            _state = state;
            _storyPath = fullPath;
            UpdateLoadedStoryDisplay();
            _gameDisplay.Model.ClearOutput();
            _gameDisplay.Model.SetStatusLine(string.Empty, "Score: 0  Moves: 0");
            _gameDisplay.FocusPrompt();
            _appStatusText.Text = $"Loaded {Path.GetFileName(fullPath)} (Z-machine version {story.Header.Version}); waiting for game-session integration.";
            RememberSelectedDirectory(FileDialogPurpose.PlayGame, fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or StoryFileFormatException or BlorbFormatException)
        {
            _appStatusText.Text = "Could not load the selected story.";
            await ShowErrorAsync("Unable to open story", $"The story could not be loaded from:\n{path}\n\n{exception.Message}");
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
        if (_story is null || _state is null)
        {
            return;
        }

        var provider = GetStorageProvider();
        if (provider is null)
        {
            await ShowErrorAsync("File picker unavailable", "This window does not have access to a file picker.");
            return;
        }

        try
        {
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

            var bytes = QuetzalCodec.Encode(_state.CaptureSaveState(), _story);
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
        if (_story is null || _state is null)
        {
            return;
        }

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
            var save = QuetzalCodec.Decode(buffer.ToArray(), _story);
            _state.ApplySaveState(save);
            _appStatusText.Text = $"Restored game from {Path.GetFileName(path)}.";
            RememberSelectedDirectory(FileDialogPurpose.Restore, path);
        }
        catch (Exception exception) when (IsFileOperationException(exception) || exception is QuetzalFormatException)
        {
            await ShowErrorAsync("Unable to restore game", exception.Message);
        }
    }

    private void OnRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_state is null)
        {
            return;
        }

        _state.Restart();
        _appStatusText.Text = "Game restarted.";
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCommandSubmitted(string command) =>
        _appStatusText.Text = "Game input will be connected when story execution is integrated in Task 26.";

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
                    new TextBlock { Text = details, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        ((Button)((StackPanel)dialog.Content).Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private static bool IsFileOperationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
}
