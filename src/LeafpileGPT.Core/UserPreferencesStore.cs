namespace LeafpileGPT.Core;

using System.Text.Json;

/// <summary>The independent file-dialog locations persisted for this user.</summary>
public sealed record UserPreferences(
    string? LastPlayGameDirectory,
    string LastSaveDirectory,
    string LastRestoreDirectory,
    ZMachineRuntimeErrorPolicy RuntimeErrorPolicy = ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType);

/// <summary>Identifies which file dialog's last-used directory should be read or updated.</summary>
public enum FileDialogPurpose
{
    PlayGame,
    Save,
    Restore
}

/// <summary>Loads and atomically persists per-user file-dialog directory preferences.</summary>
public sealed class UserPreferencesStore
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly AppDataPaths _paths;
    private UserPreferences _current;

    public UserPreferencesStore(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _current = Load();
    }

    public UserPreferences Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <summary>Gets the last-used directory, or null when Play Game has not yet been used.</summary>
    public string? GetLastUsedDirectory(FileDialogPurpose purpose)
    {
        var preferences = Current;
        return purpose switch
        {
            FileDialogPurpose.PlayGame => preferences.LastPlayGameDirectory,
            FileDialogPurpose.Save => preferences.LastSaveDirectory,
            FileDialogPurpose.Restore => preferences.LastRestoreDirectory,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown file dialog purpose.")
        };
    }

    /// <summary>Persists a selected directory independently for Play Game, Save, or Restore.</summary>
    public void RememberDirectory(FileDialogPurpose purpose, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var normalizedDirectory = Path.GetFullPath(directory);
        lock (_sync)
        {
            var updated = purpose switch
            {
                FileDialogPurpose.PlayGame => _current with { LastPlayGameDirectory = normalizedDirectory },
                FileDialogPurpose.Save => _current with { LastSaveDirectory = normalizedDirectory },
                FileDialogPurpose.Restore => _current with { LastRestoreDirectory = normalizedDirectory },
                _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown file dialog purpose.")
            };

            Save(updated);
            _current = updated;
        }
    }

    /// <summary>Persists the preferred runtime error-reporting policy.</summary>
    public void SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        lock (_sync)
        {
            var updated = _current with { RuntimeErrorPolicy = policy };
            Save(updated);
            _current = updated;
        }
    }

    private UserPreferences Load()
    {
        if (!File.Exists(_paths.PreferencesFilePath))
        {
            return new UserPreferences(null, _paths.SaveDirectory, _paths.SaveDirectory);
        }

        PreferencesDocument document;
        try
        {
            var json = File.ReadAllBytes(_paths.PreferencesFilePath);
            document = JsonSerializer.Deserialize<PreferencesDocument>(json)
                ?? throw new JsonException("The JSON document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Preferences file '{_paths.PreferencesFilePath}' is not valid JSON.", exception);
        }

        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Preferences file '{_paths.PreferencesFilePath}' uses unsupported format version {document.FormatVersion}.");
        }

        var playGame = NormalizePersistedDirectory(document.LastPlayGameDirectory);
        var save = NormalizePersistedDirectory(document.LastSaveDirectory) ?? _paths.SaveDirectory;
        var restore = NormalizePersistedDirectory(document.LastRestoreDirectory) ?? _paths.SaveDirectory;
        if (!Enum.IsDefined(document.RuntimeErrorPolicy))
        {
            throw new InvalidDataException(
                $"Preferences file '{_paths.PreferencesFilePath}' contains an unknown runtime error policy.");
        }

        return new UserPreferences(playGame, save, restore, document.RuntimeErrorPolicy);
    }

    private void Save(UserPreferences preferences)
    {
        var document = new PreferencesDocument
        {
            FormatVersion = CurrentFormatVersion,
            LastPlayGameDirectory = preferences.LastPlayGameDirectory,
            LastSaveDirectory = preferences.LastSaveDirectory,
            LastRestoreDirectory = preferences.LastRestoreDirectory,
            RuntimeErrorPolicy = preferences.RuntimeErrorPolicy
        };
        var temporaryPath = _paths.PreferencesFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _paths.PreferencesFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string? NormalizePersistedDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidDataException(
                $"Preferences file '{_paths.PreferencesFilePath}' contains a non-absolute directory path.");
        }

        return Path.GetFullPath(directory);
    }

    private sealed class PreferencesDocument
    {
        public int FormatVersion { get; init; }

        public string? LastPlayGameDirectory { get; init; }

        public string? LastSaveDirectory { get; init; }

        public string? LastRestoreDirectory { get; init; }

        public ZMachineRuntimeErrorPolicy RuntimeErrorPolicy { get; init; } = ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType;
    }
}
