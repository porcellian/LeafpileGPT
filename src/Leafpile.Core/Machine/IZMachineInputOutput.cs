namespace Leafpile.Core.Machine;

public sealed record ZMachineInputRequest(
    bool ReadCharacter,
    bool FromCommandFile,
    int MaximumCharacters,
    string InitialText,
    IReadOnlySet<byte> TerminatingCharacters,
    ushort TimeTenths,
    ushort InterruptRoutine);

public sealed record ZMachineInputResponse(string Text, byte Terminator, bool TimedOut = false);

/// <summary>Provides keyboard and optional file-backed input and output to the machine core.</summary>
public interface IZMachineInputOutput
{
    /// <summary>Gets whether timed reads and their interrupt callbacks are supported.</summary>
    bool SupportsTimedInput { get; }

    bool SupportsCommandInput { get; }

    bool SupportsTranscript { get; }

    bool SupportsCommandRecording { get; }

    /// <summary>
    /// Blocks until a line or key is accepted. For timed reads, invoke the callback on the
    /// interpreter thread at each interval and keep waiting when it returns false.
    /// </summary>
    ZMachineInputResponse ReadInput(ZMachineInputRequest request, Func<bool>? timedInterrupt);

    /// <summary>Displays text produced by a timed-input interrupt while input is still pending.</summary>
    void WriteScreenOutput(string text);

    void WriteTranscript(string text);

    void WriteCommand(string text);

    void WriteKeypress(byte zsciiCode);

    void ReportFileAccessUnavailable(string feature);
}
