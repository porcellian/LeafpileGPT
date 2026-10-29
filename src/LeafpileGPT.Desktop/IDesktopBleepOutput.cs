namespace LeafpileGPT.Desktop;

/// <summary>Produces the two built-in Z-machine bleeps without decoding story resources.</summary>
public interface IDesktopBleepOutput : IDisposable
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    void Play(ushort number);
}
