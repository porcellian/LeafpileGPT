namespace LeafpileGPT.Core;

/// <summary>Resolves and creates LeafpileGPT's per-user application data directories.</summary>
public sealed class AppDataPaths
{
    public AppDataPaths(string? baseDirectory = null)
    {
        var basePath = baseDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("The operating system did not provide a local application-data directory.");
        }

        RootDirectory = Path.Combine(Path.GetFullPath(basePath), ApplicationInfo.Name);
        SaveDirectory = Path.Combine(RootDirectory, "Save");
        PreferencesDirectory = Path.Combine(RootDirectory, "Preferences");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        AuditsDirectory = Path.Combine(RootDirectory, "Audits");
        PreferencesFilePath = Path.Combine(PreferencesDirectory, "preferences.json");

        Directory.CreateDirectory(SaveDirectory);
        Directory.CreateDirectory(PreferencesDirectory);
    }

    /// <summary>Gets the root LeafpileGPT directory under OS-standard local application data.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the default directory for Quetzal files.</summary>
    public string SaveDirectory { get; }

    /// <summary>Gets the directory containing persistent preferences.</summary>
    public string PreferencesDirectory { get; }

    /// <summary>Gets the directory used for optional structured diagnostic logs.</summary>
    public string LogsDirectory { get; }

    /// <summary>Gets the default directory for exported instruction audits.</summary>
    public string AuditsDirectory { get; }

    /// <summary>Gets the JSON preferences file path.</summary>
    public string PreferencesFilePath { get; }

    /// <summary>Gets the OS-standard local application-data base directory.</summary>
    public static string GetDefaultBaseDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("The operating system did not provide a local application-data directory.");
        }

        return Path.GetFullPath(basePath);
    }
}
