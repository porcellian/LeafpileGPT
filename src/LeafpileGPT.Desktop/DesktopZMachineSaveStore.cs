using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

/// <summary>Provides the VM's non-interactive save opcodes with a story-specific app-data save.</summary>
public sealed class DesktopZMachineSaveStore : IZMachineSaveStore
{
    private readonly string _directory;
    private readonly string _defaultBaseName;

    public DesktopZMachineSaveStore(string directory, string storyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyName);
        _directory = Path.GetFullPath(directory);
        _defaultBaseName = MakeSafeBaseName(storyName);
    }

    public bool Save(ReadOnlyMemory<byte> ifzsImage, ZMachineSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(ResolvePath(options), ifzsImage.ToArray());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public byte[]? Restore(ZMachineSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = ResolvePath(options);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public string ResolvePath(ZMachineSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var baseName = string.IsNullOrWhiteSpace(options.SuggestedName)
            ? _defaultBaseName
            : MakeSafeBaseName(options.SuggestedName);
        return Path.Combine(_directory, baseName + ".ifzs");
    }

    internal static string MakeTranscriptSafeName(string name) => MakeSafeBaseName(name);

    private static string MakeSafeBaseName(string name)
    {
        var fileName = Path.GetFileNameWithoutExtension(name.Trim());
        var safeCharacters = fileName.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray();
        var safeName = new string(safeCharacters).Trim('.', '_', '-');
        return string.IsNullOrEmpty(safeName) ? "game" : safeName;
    }
}
