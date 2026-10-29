using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void CreatesSaveAndPreferencesDirectoriesUnderAppDataRoot()
    {
        using var fixture = new TemporaryDirectory();

        var paths = new AppDataPaths(fixture.Path);

        Assert.Equal(Path.Combine(fixture.Path, ApplicationInfo.Name), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Save"), paths.SaveDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Preferences"), paths.PreferencesDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "Logs"), paths.LogsDirectory);
        Assert.True(Directory.Exists(paths.SaveDirectory));
        Assert.True(Directory.Exists(paths.PreferencesDirectory));
        Assert.Equal(Path.Combine(paths.PreferencesDirectory, "preferences.json"), paths.PreferencesFilePath);
    }

    [Fact]
    public void SeedsSaveAndRestoreDirectoriesButLeavesPlayGameUnselected()
    {
        using var fixture = new TemporaryDirectory();
        var paths = new AppDataPaths(fixture.Path);

        var preferences = new UserPreferencesStore(paths);

        Assert.Null(preferences.GetLastUsedDirectory(FileDialogPurpose.PlayGame));
        Assert.Equal(paths.SaveDirectory, preferences.GetLastUsedDirectory(FileDialogPurpose.Save));
        Assert.Equal(paths.SaveDirectory, preferences.GetLastUsedDirectory(FileDialogPurpose.Restore));
        Assert.Equal(ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType, preferences.Current.RuntimeErrorPolicy);
        Assert.False(File.Exists(paths.PreferencesFilePath));
    }

    [Fact]
    public void PersistsEachFileDialogDirectoryIndependentlyAcrossStoreInstances()
    {
        using var fixture = new TemporaryDirectory();
        var paths = new AppDataPaths(fixture.Path);
        var preferences = new UserPreferencesStore(paths);
        var playDirectory = Directory.CreateDirectory(Path.Combine(fixture.Path, "Stories With Spaces")).FullName;
        var saveDirectory = Directory.CreateDirectory(Path.Combine(fixture.Path, "Manual Saves")).FullName;
        var restoreDirectory = Directory.CreateDirectory(Path.Combine(fixture.Path, "Old Saves")).FullName;

        preferences.RememberDirectory(FileDialogPurpose.PlayGame, playDirectory);
        preferences.RememberDirectory(FileDialogPurpose.Save, saveDirectory);
        preferences.RememberDirectory(FileDialogPurpose.Restore, restoreDirectory);

        var reloaded = new UserPreferencesStore(new AppDataPaths(fixture.Path));

        Assert.Equal(playDirectory, reloaded.GetLastUsedDirectory(FileDialogPurpose.PlayGame));
        Assert.Equal(saveDirectory, reloaded.GetLastUsedDirectory(FileDialogPurpose.Save));
        Assert.Equal(restoreDirectory, reloaded.GetLastUsedDirectory(FileDialogPurpose.Restore));
        Assert.Empty(Directory.GetFiles(paths.PreferencesDirectory, "*.tmp"));
    }

    [Fact]
    public void RuntimeErrorPolicyDefaultsToFirstPerTypeAndPersistsAcrossLaunches()
    {
        using var fixture = new TemporaryDirectory();
        var paths = new AppDataPaths(fixture.Path);
        var preferences = new UserPreferencesStore(paths);
        Assert.Equal(ZMachineRuntimeErrorPolicy.ReportFirstPerErrorType, preferences.Current.RuntimeErrorPolicy);

        preferences.SetRuntimeErrorPolicy(ZMachineRuntimeErrorPolicy.StopOnAnyError);

        var restored = new UserPreferencesStore(new AppDataPaths(fixture.Path));
        Assert.Equal(ZMachineRuntimeErrorPolicy.StopOnAnyError, restored.Current.RuntimeErrorPolicy);
    }

    [Fact]
    public void MissingSaveAndRestoreValuesInExistingDocumentUseDefaultSaveDirectory()
    {
        using var fixture = new TemporaryDirectory();
        var paths = new AppDataPaths(fixture.Path);
        File.WriteAllText(paths.PreferencesFilePath, """{"FormatVersion":1,"LastPlayGameDirectory":null}""");

        var preferences = new UserPreferencesStore(paths);

        Assert.Equal(paths.SaveDirectory, preferences.GetLastUsedDirectory(FileDialogPurpose.Save));
        Assert.Equal(paths.SaveDirectory, preferences.GetLastUsedDirectory(FileDialogPurpose.Restore));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"FormatVersion\":2}")]
    [InlineData("{\"FormatVersion\":1,\"LastSaveDirectory\":\"relative/path\"}")]
    [InlineData("{\"FormatVersion\":1,\"RuntimeErrorPolicy\":99}")]
    public void InvalidPreferenceDocumentFailsWithActionableError(string content)
    {
        using var fixture = new TemporaryDirectory();
        var paths = new AppDataPaths(fixture.Path);
        File.WriteAllText(paths.PreferencesFilePath, content);

        var exception = Assert.Throws<InvalidDataException>(() => new UserPreferencesStore(paths));

        Assert.Contains(paths.PreferencesFilePath, exception.Message);
    }

    [Fact]
    public void DefaultBaseDirectoryComesFromOperatingSystemLocalAppData()
    {
        Assert.Equal(
            Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            AppDataPaths.GetDefaultBaseDirectory());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("leafpile-app-data-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
