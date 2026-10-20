namespace Leafpile.Core.Machine;

/// <summary>Describes one background sound request in Z-machine volume and repeat units.</summary>
public sealed record ZMachineSoundRequest(
    ushort Number,
    int Volume,
    int? Repeats,
    ushort CompletionRoutine);

/// <summary>Provides Z-machine bleeps and optional story-specific sound playback.</summary>
public interface IZMachineSound
{
    /// <summary>Gets whether non-bleep story sounds can be played.</summary>
    bool SupportsSoundEffects { get; }

    void Prepare(ushort soundNumber);

    /// <summary>Starts playback of a sound request.</summary>
    /// <remarks>
    /// A null repeat count means repeat indefinitely. Volume is 1–8, or -1 for the
    /// standard's loudest-possible value. Call <paramref name="completed"/> only after
    /// natural completion; stopping or interrupting a sound must not call it. Resource
    /// lookup and sample/music channel arbitration belong to the host.
    /// </remarks>
    void Play(ZMachineSoundRequest request, Action? completed);

    /// <summary>Stops the numbered sound, or all sounds when the number is null.</summary>
    void Stop(ushort? soundNumber);

    /// <summary>Stops and releases the numbered sound, or all sounds when null.</summary>
    void Finish(ushort? soundNumber);
}
