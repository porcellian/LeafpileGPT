using SDL3;

namespace LeafpileGPT.Desktop;

/// <summary>Queues short PCM bleeps through SDL's cross-platform default playback device.</summary>
public sealed class SdlDesktopBleepOutput : IDesktopBleepOutput
{
    private const int SampleRate = 44_100;
    private const double DurationSeconds = 0.12;
    private const double Volume = 0.24;

    private IntPtr _stream;
    private bool _audioSubsystemInitialized;
    private int _disposed;

    public SdlDesktopBleepOutput()
    {
        try
        {
            if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
            {
                throw new InvalidOperationException($"Could not initialize audio: {SDL.GetError()}");
            }

            _audioSubsystemInitialized = true;
            var specification = new SDL.AudioSpec
            {
                Format = SDL.AudioFormat.AudioS16LE,
                Channels = 1,
                Freq = SampleRate
            };
            _stream = SDL.OpenAudioDeviceStream(
                SDL.AudioDeviceDefaultPlayback,
                specification,
                null,
                IntPtr.Zero);
            if (_stream == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Could not open the audio output device: {SDL.GetError()}");
            }

            if (!SDL.ResumeAudioStreamDevice(_stream))
            {
                throw new InvalidOperationException($"Could not start the audio output device: {SDL.GetError()}");
            }

            IsAvailable = true;
        }
        catch (Exception exception)
        {
            if (_stream != IntPtr.Zero)
            {
                SDL.DestroyAudioStream(_stream);
                _stream = IntPtr.Zero;
            }

            if (_audioSubsystemInitialized)
            {
                SDL.QuitSubSystem(SDL.InitFlags.Audio);
                _audioSubsystemInitialized = false;
            }

            UnavailableReason = exception.Message;
        }
    }

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public void Play(ushort number)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!IsAvailable)
        {
            throw new InvalidOperationException(UnavailableReason ?? "The audio output device is unavailable.");
        }

        if (number is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Only standard bleep effects 1 and 2 are supported.");
        }

        var samples = CreateSamples(number == 1 ? 880 : 660);
        if (!SDL.PutAudioStreamData(_stream, samples, samples.Length))
        {
            throw new InvalidOperationException($"Could not queue the sound effect: {SDL.GetError()}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_stream != IntPtr.Zero)
        {
            SDL.DestroyAudioStream(_stream);
        }

        if (_audioSubsystemInitialized)
        {
            SDL.QuitSubSystem(SDL.InitFlags.Audio);
            _audioSubsystemInitialized = false;
        }
    }

    private static byte[] CreateSamples(int frequency)
    {
        var sampleCount = (int)(SampleRate * DurationSeconds);
        var bytes = new byte[sampleCount * sizeof(short)];
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var time = (double)sample / SampleRate;
            var fade = Math.Min(1, Math.Min(time / 0.008, (DurationSeconds - time) / 0.008));
            var value = (short)(Math.Sin(2 * Math.PI * frequency * time) * short.MaxValue * Volume * fade);
            bytes[sample * 2] = (byte)value;
            bytes[(sample * 2) + 1] = (byte)(value >> 8);
        }

        return bytes;
    }
}
