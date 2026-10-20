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

internal sealed class DesktopSaveFileAccess(Window owner) : IZMachineSaveFileAccess
{
    public bool WriteSaveFile(ReadOnlyMemory<byte> saveFile) => RunOnUi(async () =>
    {
        var target = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Story",
            SuggestedFileName = "save.sav",
            DefaultExtension = "sav",
            FileTypeChoices = [new FilePickerFileType("Quetzal save") { Patterns = ["*.sav", "*.qzl"] }]
        });
        if (target is null) return false;
        await using var stream = await target.OpenWriteAsync();
        await stream.WriteAsync(saveFile);
        return true;
    });

    public ReadOnlyMemory<byte>? ReadSaveFile() => RunOnUi<ReadOnlyMemory<byte>?>(async () =>
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore Story",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Quetzal save") { Patterns = ["*.sav", "*.qzl"] }]
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    });

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

internal sealed class DesktopSoundHost : IZMachineSound
{
    public bool SupportsSoundEffects => false;
    public void Prepare(ushort soundNumber) => _ = soundNumber;
    public void Play(ZMachineSoundRequest request, Action? completed) { _ = request; _ = completed; }
    public void Stop(ushort? soundNumber) => _ = soundNumber;
    public void Finish(ushort? soundNumber) => _ = soundNumber;
}
