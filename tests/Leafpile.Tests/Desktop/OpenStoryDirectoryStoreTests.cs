using System.Reflection;
using Avalonia.Platform.Storage;
using Leafpile.Desktop;

namespace Leafpile.Tests.Desktop;

public sealed class OpenStoryDirectoryStoreTests
{
    [Fact]
    public void DefaultPreferencePathUsesSeparatePerUserSettingsFile()
    {
        var path = OpenStoryDirectoryStore.GetDefaultPreferenceFilePath("/user/local-data");

        Assert.Equal(Path.Combine("/user/local-data", "LeafpileGPT", "Preferences", "open-story-directory.txt"), path);
    }

    [Fact]
    public void FirstUseHasNoSavedDirectoryAndKeepsPickerDefault()
    {
        var store = new OpenStoryDirectoryStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "open-story-directory.txt"));

        var result = store.Read();
        var options = MainWindow.CreateOpenStoryPickerOptions(startLocation: null);

        Assert.Null(result.Directory);
        Assert.Null(result.Warning);
        Assert.Null(options.SuggestedStartLocation);
        Assert.False(options.AllowMultiple);
        var fileType = Assert.Single(options.FileTypeFilter!);
        Assert.Equal(["*.z1", "*.z2", "*.z3", "*.z4", "*.z5", "*.z6", "*.z7", "*.z8", "*.blorb", "*.blb", "*.*"], fileType.Patterns);
    }

    [Fact]
    public void OpenStoryPickerUsesRememberedFolder()
    {
        var folder = DispatchProxy.Create<IStorageFolder, EmptyStorageFolderProxy>();

        var options = MainWindow.CreateOpenStoryPickerOptions(folder);

        Assert.Same(folder, options.SuggestedStartLocation);
    }

    [Fact]
    public void SelectedStoryFolderIsRememberedAndLoadedByANewStore()
    {
        var root = CreateTempDirectory();
        try
        {
            var storyDirectory = Path.Combine(root, "stories");
            Directory.CreateDirectory(storyDirectory);
            var preferencePath = Path.Combine(root, "settings", "open-story-directory.txt");
            var firstLaunch = new OpenStoryDirectoryStore(preferencePath);

            var save = firstLaunch.RememberSelectedStory(Path.Combine(storyDirectory, "adventure.z3"));
            var nextLaunch = new OpenStoryDirectoryStore(preferencePath).Read();

            Assert.Null(save.Warning);
            Assert.Equal(storyDirectory, save.Directory);
            Assert.Equal(storyDirectory, nextLaunch.Directory);
            Assert.Null(nextLaunch.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingSavedFolderIsClearedAndUsesPickerDefault()
    {
        var root = CreateTempDirectory();
        try
        {
            var storyDirectory = Path.Combine(root, "stories");
            Directory.CreateDirectory(storyDirectory);
            var preferencePath = Path.Combine(root, "settings", "open-story-directory.txt");
            var store = new OpenStoryDirectoryStore(preferencePath);
            store.RememberSelectedStory(Path.Combine(storyDirectory, "adventure.z3"));
            Directory.Delete(storyDirectory);

            var result = store.Read();

            Assert.Null(result.Directory);
            Assert.Contains("unavailable", result.Warning, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(preferencePath));
            Assert.Null(MainWindow.CreateOpenStoryPickerOptions(startLocation: null).SuggestedStartLocation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferenceReadFailureIsReportedWithoutThrowing()
    {
        var root = CreateTempDirectory();
        try
        {
            var preferencePath = Path.Combine(root, "preference-directory");
            Directory.CreateDirectory(preferencePath);

            var result = new OpenStoryDirectoryStore(preferencePath).Read();

            Assert.Null(result.Directory);
            Assert.NotNull(result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferenceWriteFailureIsReportedWithoutThrowing()
    {
        var root = CreateTempDirectory();
        try
        {
            var settingsParent = Path.Combine(root, "not-a-directory");
            File.WriteAllText(settingsParent, "block directory creation");
            var preferencePath = Path.Combine(settingsParent, "open-story-directory.txt");

            var result = new OpenStoryDirectoryStore(preferencePath).RememberSelectedStory(Path.Combine(root, "adventure.z3"));

            Assert.Null(result.Directory);
            Assert.NotNull(result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnavailableLocalApplicationDataIsReportedWithoutThrowing()
    {
        var store = new OpenStoryDirectoryStore(OpenStoryDirectoryStore.GetDefaultPreferenceFilePath(""));

        var read = store.Read();
        var write = store.RememberSelectedStory("/stories/adventure.z3");

        Assert.Null(read.Directory);
        Assert.NotNull(read.Warning);
        Assert.NotNull(write.Warning);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leafpile-open-story-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    public class EmptyStorageFolderProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
