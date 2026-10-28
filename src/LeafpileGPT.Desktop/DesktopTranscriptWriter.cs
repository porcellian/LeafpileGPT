using System.Globalization;

namespace LeafpileGPT.Desktop;

/// <summary>Writes Z-machine stream 2 output to a per-session text transcript in app data.</summary>
public sealed class DesktopTranscriptWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _fileName;
    private readonly object _sync = new();
    private StreamWriter? _writer;
    private bool _disabled;

    public DesktopTranscriptWriter(string directory, string storyName, DateTimeOffset? startedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(storyName);
        _directory = Path.GetFullPath(directory);
        var baseName = Path.GetFileNameWithoutExtension(storyName);
        var timestamp = (startedAt ?? DateTimeOffset.UtcNow).ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        _fileName = $"{DesktopZMachineSaveStore.MakeTranscriptSafeName(baseName)}-{timestamp}-{Guid.NewGuid():N}.txt";
    }

    public string? FilePath { get; private set; }

    public event Action<Exception>? WriteFailed;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_sync)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                if (_writer is null)
                {
                    Directory.CreateDirectory(_directory);
                    FilePath = Path.Combine(_directory, _fileName);
                    _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                }

                _writer.Write(text);
                _writer.Flush();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _disabled = true;
                _writer?.Dispose();
                _writer = null;
                WriteFailed?.Invoke(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
