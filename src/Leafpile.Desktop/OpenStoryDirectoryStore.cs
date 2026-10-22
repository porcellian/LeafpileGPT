namespace Leafpile.Desktop;

internal sealed record OpenStoryDirectoryResult(string? Directory, string? Warning = null);

internal sealed class OpenStoryDirectoryStore(string? preferenceFilePath)
{
    public static string? GetDefaultPreferenceFilePath(string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory)) return null;

        return Path.Combine(localApplicationDataDirectory, "LeafpileGPT", "Preferences", "open-story-directory.txt");
    }

    public OpenStoryDirectoryResult Read()
    {
        if (preferenceFilePath is null)
        {
            return new(null, "Could not determine a per-user settings directory; Open Story will use the picker default.");
        }

        string directory;
        try
        {
            directory = File.ReadAllText(preferenceFilePath);
        }
        catch (FileNotFoundException)
        {
            return new(null);
        }
        catch (DirectoryNotFoundException)
        {
            return new(null);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(null, $"Could not read the remembered story folder; Open Story will use the picker default. {exception.Message}");
        }

        directory = directory.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(directory)) return new(null);

        try
        {
            directory = Path.GetFullPath(directory);
            if (System.IO.Directory.Exists(directory)) return new(directory);

            File.Delete(preferenceFilePath);
            return new(null, "The previously used story folder is unavailable; its saved location was cleared.");
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(null, $"Could not use the remembered story folder; Open Story will use the picker default. {exception.Message}");
        }
    }

    public OpenStoryDirectoryResult RememberSelectedStory(string localStoryPath)
    {
        if (preferenceFilePath is null)
        {
            return new(null, "Could not determine a per-user settings directory; this story folder will not be remembered.");
        }

        try
        {
            var storyDirectory = Path.GetDirectoryName(Path.GetFullPath(localStoryPath));
            var preferencesDirectory = Path.GetDirectoryName(preferenceFilePath);
            if (storyDirectory is null || preferencesDirectory is null)
            {
                return new(null, "Could not determine the selected story or settings folder; this story folder will not be remembered.");
            }

            System.IO.Directory.CreateDirectory(preferencesDirectory);
            File.WriteAllText(preferenceFilePath, storyDirectory);
            return new(storyDirectory);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(null, $"Could not remember the selected story folder. {exception.Message}");
        }
    }

    public OpenStoryDirectoryResult Clear()
    {
        if (preferenceFilePath is null) return new(null);

        try
        {
            File.Delete(preferenceFilePath);
            return new(null);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new(null, $"Could not clear the unavailable story folder preference. {exception.Message}");
        }
    }

    private static bool IsStorageException(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
