using System.Reflection;
using Avalonia.Platform.Storage;
using Leafpile.Desktop;

namespace Leafpile.Tests.Desktop;

public sealed class SaveDirectoryTests
{
    [Fact]
    public void Resolve_UsesConfiguredDirectoryExactly()
    {
        var configuredDirectory = Path.Combine(Path.GetTempPath(), "LeafpileGPT-override");

        var actual = SaveDirectoryResolver.Resolve(configuredDirectory, "/user/app-data");

        Assert.Equal(Path.GetFullPath(configuredDirectory), actual);
    }

    [Fact]
    public void Resolve_UsesLeafpileFolderUnderLocalApplicationDataByDefault()
    {
        var actual = SaveDirectoryResolver.Resolve(null, "/user/app-data");

        Assert.Equal(Path.Combine("/user/app-data", "LeafpileGPT"), actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_TreatsBlankOverrideAsUnset(string configuredDirectory)
    {
        var actual = SaveDirectoryResolver.Resolve(configuredDirectory, "/user/app-data");

        Assert.Equal(Path.Combine("/user/app-data", "LeafpileGPT"), actual);
    }

    [Fact]
    public void Resolve_FailsWhenOperatingSystemProvidesNoLocalApplicationDataDirectory()
    {
        Assert.Throws<InvalidOperationException>(() => SaveDirectoryResolver.Resolve(null, ""));
    }

    [Fact]
    public void EnsureExists_CreatesTheSaveDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LeafpileGPT-tests", Guid.NewGuid().ToString("N"));
        try
        {
            SaveDirectoryResolver.EnsureExists(directory);

            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void SavePickerOptions_UseStartLocationAndPreserveSaveSettings()
    {
        var folder = DispatchProxy.Create<IStorageFolder, EmptyStorageFolderProxy>();

        var options = DesktopSaveFileAccess.CreateSavePickerOptions(folder);

        Assert.Same(folder, options.SuggestedStartLocation);
        Assert.Equal("Save Story", options.Title);
        Assert.Equal("save.sav", options.SuggestedFileName);
        Assert.Equal("sav", options.DefaultExtension);
        var saveFileType = Assert.Single(options.FileTypeChoices!);
        Assert.Equal(new[] { "*.sav", "*.qzl" }, saveFileType.Patterns);
    }

    [Fact]
    public void RestorePickerOptions_UseStartLocationAndPreserveRestoreSettings()
    {
        var folder = DispatchProxy.Create<IStorageFolder, EmptyStorageFolderProxy>();

        var options = DesktopSaveFileAccess.CreateOpenPickerOptions(folder);

        Assert.Same(folder, options.SuggestedStartLocation);
        Assert.Equal("Restore Story", options.Title);
        Assert.False(options.AllowMultiple);
        var restoreFileType = Assert.Single(options.FileTypeFilter!);
        Assert.Equal(new[] { "*.sav", "*.qzl" }, restoreFileType.Patterns);
    }

    public class EmptyStorageFolderProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
