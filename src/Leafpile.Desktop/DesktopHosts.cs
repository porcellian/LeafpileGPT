using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Leafpile.Core.Machine;

namespace Leafpile.Desktop;

internal sealed class DesktopInputOutputHost(Action<ZMachineInputRequest, TaskCompletionSource<ZMachineInputResponse>> requestInput) : IZMachineInputOutput
{
    private TaskCompletionSource<ZMachineInputResponse>? _pendingInput;

    public bool SupportsTimedInput => false;
    public bool SupportsCommandInput => false;
    public bool SupportsTranscript => false;
    public bool SupportsCommandRecording => false;

    public ZMachineInputResponse ReadInput(ZMachineInputRequest request, Func<bool>? timedInterrupt)
    {
        _ = timedInterrupt;
        var completion = new TaskCompletionSource<ZMachineInputResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pendingInput, completion)?.TrySetResult(new ZMachineInputResponse(string.Empty, 13));
        Dispatcher.UIThread.Post(() => requestInput(request, completion));
        return completion.Task.GetAwaiter().GetResult();
    }

    public void CancelPendingInput() => Interlocked.Exchange(ref _pendingInput, null)?.TrySetResult(new ZMachineInputResponse(string.Empty, 13));

    public void WriteScreenOutput(string text) => _ = text;
    public void WriteTranscript(string text) => _ = text;
    public void WriteCommand(string text) => _ = text;
    public void WriteKeypress(byte zsciiCode) => _ = zsciiCode;
    public void ReportFileAccessUnavailable(string feature) => _ = feature;
}

internal sealed class DesktopSaveFileAccess(MainWindow owner) : IZMachineSaveFileAccess
{
    public bool WriteSaveFile(ReadOnlyMemory<byte> saveFile) => RunOnUi(async () =>
    {
        var startLocation = await GetSaveStartLocationAsync();
        var target = await owner.StorageProvider.SaveFilePickerAsync(CreateSavePickerOptions(startLocation));
        if (target is null) return false;
        await using var stream = await target.OpenWriteAsync();
        await stream.WriteAsync(saveFile);
        return true;
    });

    public ReadOnlyMemory<byte>? ReadSaveFile() => RunOnUi<ReadOnlyMemory<byte>?>(async () =>
    {
        var startLocation = await GetSaveStartLocationAsync();
        var files = await owner.StorageProvider.OpenFilePickerAsync(CreateOpenPickerOptions(startLocation));
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    });

    internal static FilePickerSaveOptions CreateSavePickerOptions(IStorageFolder startLocation) => new()
    {
        Title = "Save Story",
        SuggestedStartLocation = startLocation,
        SuggestedFileName = "save.sav",
        DefaultExtension = "sav",
        FileTypeChoices = [new FilePickerFileType("Quetzal save") { Patterns = ["*.sav", "*.qzl"] }]
    };

    internal static FilePickerOpenOptions CreateOpenPickerOptions(IStorageFolder startLocation) => new()
    {
        Title = "Restore Story",
        SuggestedStartLocation = startLocation,
        AllowMultiple = false,
        FileTypeFilter = [new FilePickerFileType("Quetzal save") { Patterns = ["*.sav", "*.qzl"] }]
    };

    private async Task<IStorageFolder> GetSaveStartLocationAsync()
    {
        try
        {
            var directory = SaveDirectoryResolver.Resolve(
                Environment.GetEnvironmentVariable("LEAFPILEGPT_HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            SaveDirectoryResolver.EnsureExists(directory);
            return await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(directory))
                ?? throw new DirectoryNotFoundException($"The save directory '{directory}' is unavailable to the file picker.");
        }
        catch (Exception exception)
        {
            var message = $"Could not prepare the Quetzal save directory: {exception.Message}";
            owner.ReportRuntimeError(message);
            throw new InvalidOperationException(message, exception);
        }
    }

    private static T RunOnUi<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action().GetAwaiter().GetResult();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { completion.TrySetResult(await action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task.GetAwaiter().GetResult();
    }
}

internal static class SaveDirectoryResolver
{
    public static string Resolve(string? configuredDirectory, string localApplicationDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory)) return Path.GetFullPath(configuredDirectory);
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
            throw new InvalidOperationException("The operating system did not provide a local application-data directory.");

        return Path.Combine(localApplicationDataDirectory, "LeafpileGPT");
    }

    public static void EnsureExists(string directory) => Directory.CreateDirectory(directory);
}

internal sealed class DesktopSoundHost : IZMachineSound
{
    public bool SupportsSoundEffects => false;
    public void Prepare(ushort soundNumber) => _ = soundNumber;
    public void Play(ZMachineSoundRequest request, Action? completed) { _ = request; _ = completed; }
    public void Stop(ushort? soundNumber) => _ = soundNumber;
    public void Finish(ushort? soundNumber) => _ = soundNumber;
}
