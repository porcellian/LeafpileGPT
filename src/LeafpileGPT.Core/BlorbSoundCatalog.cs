namespace LeafpileGPT.Core;

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

public enum BlorbSoundFormat
{
    Aiff,
    OggVorbis,
    Module,
    Song
}

/// <summary>Blorb compatibility channel used to arbitrate concurrent Z-machine sounds.</summary>
public enum BlorbSoundChannel
{
    Sample,
    Music
}

public sealed record BlorbSongSample(byte Instrument, uint SoundNumber, BlorbSoundResource Resource);

/// <summary>A validated sound resource; encoded payloads remain available to a host audio decoder.</summary>
public sealed record BlorbSoundResource(
    uint Number,
    BlorbSoundFormat Format,
    BlorbSoundChannel Channel,
    ReadOnlyMemory<byte> EncodedData,
    ImmutableArray<BlorbSongSample> Samples);

/// <summary>Resolves indexed Blorb sounds and V3 loop hints without depending on a platform audio stack.</summary>
public sealed class BlorbSoundCatalog
{
    private readonly ImmutableDictionary<uint, BlorbSoundResource> _sounds;
    private readonly ImmutableDictionary<uint, bool> _versionThreePlayOnce;

    internal BlorbSoundCatalog(ImmutableArray<BlorbResource> resources, ImmutableArray<BlorbChunk> chunks)
    {
        var sounds = ImmutableDictionary.CreateBuilder<uint, BlorbSoundResource>();
        foreach (var resource in resources.Where(item => item.Usage == "Snd "))
        {
            var format = ParseFormat(resource);
            var channel = format == BlorbSoundFormat.Aiff ? BlorbSoundChannel.Sample : BlorbSoundChannel.Music;
            sounds.Add(resource.Number, new BlorbSoundResource(
                resource.Number, format, channel, resource.Data, ImmutableArray<BlorbSongSample>.Empty));
        }

        foreach (var pair in sounds.ToArray())
        {
            if (pair.Value.Format != BlorbSoundFormat.Song)
            {
                continue;
            }

            var samples = ParseSongSamples(pair.Value, sounds);
            sounds[pair.Key] = pair.Value with { Samples = samples };
        }

        _sounds = sounds.ToImmutable();
        _versionThreePlayOnce = ParseLoops(chunks, _sounds);
    }

    public int Count => _sounds.Count;

    public ImmutableArray<BlorbSoundResource> Resources => _sounds.Values.OrderBy(item => item.Number).ToImmutableArray();

    public bool TryGetSound(uint number, out BlorbSoundResource? resource) => _sounds.TryGetValue(number, out resource);

    /// <summary>Returns V3's Blorb loop override; absent entries mean one play.</summary>
    public bool PlaysOnceInVersionThree(uint number) => !_versionThreePlayOnce.TryGetValue(number, out var once) || once;

    private static BlorbSoundFormat ParseFormat(BlorbResource resource)
    {
        var data = resource.Data.Span;
        switch (resource.Chunk.Type)
        {
            case "FORM":
                if (data.Length < 12 || ReadFourCc(data, 0) != "AIFF"
                    || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != data.Length - 8)
                {
                    throw new BlorbFormatException($"AIFF sound resource {resource.Number} has an invalid FORM AIFF payload.");
                }

                return BlorbSoundFormat.Aiff;
            case "OGGV":
                if (data.Length < 4 || ReadFourCc(data, 0) != "OggS")
                {
                    throw new BlorbFormatException($"Ogg sound resource {resource.Number} does not begin with the Ogg capture pattern.");
                }

                return BlorbSoundFormat.OggVorbis;
            case "MOD ":
                if (data.IsEmpty)
                {
                    throw new BlorbFormatException($"Module sound resource {resource.Number} is empty.");
                }

                return BlorbSoundFormat.Module;
            case "SONG":
                if (data.Length < 1084)
                {
                    throw new BlorbFormatException($"Legacy SONG sound resource {resource.Number} is shorter than its 1084-byte module header.");
                }

                return BlorbSoundFormat.Song;
            default:
                throw new BlorbFormatException(
                    $"Sound resource {resource.Number} uses unsupported chunk type '{resource.Chunk.Type}'.");
        }
    }

    private static ImmutableArray<BlorbSongSample> ParseSongSamples(
        BlorbSoundResource song,
        ImmutableDictionary<uint, BlorbSoundResource>.Builder sounds)
    {
        var result = ImmutableArray.CreateBuilder<BlorbSongSample>();
        var data = song.EncodedData.Span;
        for (var index = 0; index < 31; index++)
        {
            var field = data.Slice(20 + (index * 30), 22);
            var terminator = field.IndexOf((byte)0);
            if (terminator >= 0)
            {
                field = field[..terminator];
            }

            var name = Encoding.ASCII.GetString(field).TrimEnd(' ', '\0');
            if (name.Length == 0)
            {
                continue;
            }

            if (!name.StartsWith("SND", StringComparison.Ordinal)
                || !uint.TryParse(name.AsSpan(3), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                throw new BlorbFormatException(
                    $"SONG resource {song.Number} instrument {index + 1} has invalid AIFF sample reference '{name}'.");
            }

            if (!sounds.TryGetValue(number, out var sample) || sample.Format != BlorbSoundFormat.Aiff)
            {
                throw new BlorbFormatException(
                    $"SONG resource {song.Number} instrument {index + 1} refers to missing or non-AIFF sound {number}.");
            }

            result.Add(new BlorbSongSample(checked((byte)(index + 1)), number, sample));
        }

        return result.ToImmutable();
    }

    private static ImmutableDictionary<uint, bool> ParseLoops(
        ImmutableArray<BlorbChunk> chunks,
        ImmutableDictionary<uint, BlorbSoundResource> sounds)
    {
        var loops = chunks.Where(chunk => chunk.Type == "Loop").ToArray();
        if (loops.Length > 1)
        {
            throw new BlorbFormatException($"An IFRS form may contain at most one Loop chunk; found {loops.Length}.");
        }

        var result = ImmutableDictionary.CreateBuilder<uint, bool>();
        if (loops.Length == 0)
        {
            return result.ToImmutable();
        }

        var data = loops[0].Data.Span;
        if (data.Length % 8 != 0)
        {
            throw new BlorbFormatException($"Loop chunk length must be a multiple of 8 bytes; found {data.Length}.");
        }

        for (var offset = 0; offset < data.Length; offset += 8)
        {
            var number = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var repeats = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
            if (repeats > 1)
            {
                throw new BlorbFormatException($"Loop chunk sound {number} has invalid repeats flag {repeats}; expected 0 or 1.");
            }

            if (!sounds.ContainsKey(number))
            {
                throw new BlorbFormatException($"Loop chunk refers to missing sound resource {number}.");
            }

            if (!result.TryAdd(number, repeats == 1))
            {
                throw new BlorbFormatException($"Loop chunk contains duplicate entry for sound resource {number}.");
            }
        }

        return result.ToImmutable();
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset) =>
        Encoding.ASCII.GetString(data.Slice(offset, 4));
}
