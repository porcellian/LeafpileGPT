namespace LeafpileGPT.Core;

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum ZMachineReplayCompletionStatus
{
    Halted,
    AwaitingInput,
    InstructionLimit,
    Faulted
}

/// <summary>A portable, deterministic set of inputs for replaying a Z-machine story.</summary>
public sealed record ZMachineReplayRoute
{
    public int SchemaVersion { get; init; } = 1;

    public string StoryPath { get; init; } = string.Empty;

    public string[] Commands { get; init; } = [];

    /// <summary>Defined ZSCII values supplied to read_char, in order.</summary>
    public ushort[] KeyPresses { get; init; } = [];

    public int RandomSeed { get; init; } = 1;

    public int MaximumInstructions { get; init; } = 1_000_000;

    public string? ExpectedCompletionStatus { get; init; }

    public string? ExpectedOutputSha256 { get; init; }

    public static ZMachineReplayRoute Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var route = JsonSerializer.Deserialize<ZMachineReplayRoute>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The replay route is empty.");
        route.Validate();
        return route;
    }

    public string ResolveStoryPath(string routeFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeFilePath);
        Validate();
        return Path.GetFullPath(StoryPath, Path.GetDirectoryName(Path.GetFullPath(routeFilePath))!);
    }

    public void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidDataException($"Replay route schema version {SchemaVersion} is not supported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(StoryPath);
        if (Commands is null || Commands.Any(command => command is null))
        {
            throw new InvalidDataException("Replay commands cannot contain null values.");
        }

        if (KeyPresses is null || KeyPresses.Any(key => key > byte.MaxValue))
        {
            throw new InvalidDataException("Replay key presses must be byte-sized ZSCII values.");
        }

        if (MaximumInstructions < 1)
        {
            throw new InvalidDataException("MaximumInstructions must be greater than zero.");
        }

        if (ExpectedCompletionStatus is { } expectedStatus
            && (!Enum.TryParse<ZMachineReplayCompletionStatus>(expectedStatus, ignoreCase: true, out var status)
                || !Enum.IsDefined(status)))
        {
            throw new InvalidDataException($"Replay completion status '{expectedStatus}' is not recognized.");
        }

        if (ExpectedOutputSha256 is { } outputHash
            && (outputHash.Length != 64 || !outputHash.All(Uri.IsHexDigit)))
        {
            throw new InvalidDataException("ExpectedOutputSha256 must contain 64 hexadecimal characters.");
        }
    }

    internal static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions(indented: true);

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        var options = new JsonSerializerOptions { WriteIndented = indented, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public sealed record ZMachineOpcodeTraceEntry(
    long Sequence,
    string ProgramCounter,
    InstructionForm Form,
    OpcodeClass OpcodeClass,
    byte Opcode,
    string Mnemonic,
    ImmutableArray<DecodedOperand> Operands,
    byte? StoreVariable,
    DecodedBranch? Branch,
    string NextProgramCounter,
    string ProgramCounterAfter,
    bool IsHalted);

public sealed record ZMachineOpcodeInventoryEntry(
    InstructionForm Form,
    byte Opcode,
    string Mnemonic,
    long Count,
    long FirstSequence,
    string FirstProgramCounter);

public sealed record ZMachineReplayResult(
    ZMachineReplayCompletionStatus CompletionStatus,
    byte Version,
    ushort ReleaseNumber,
    string SerialNumber,
    string StorySha256,
    long CommandsConsumed,
    long KeyPressesConsumed,
    long MorePromptsAcknowledged,
    long InstructionsExecuted,
    string FinalProgramCounter,
    string FinalStateSha256,
    string Output,
    string OutputSha256,
    string? Error,
    bool RoutePassed,
    string? RouteFailure,
    ImmutableArray<ZMachineOpcodeInventoryEntry> OpcodeInventory);

/// <summary>Replays scripted input through the same decoder and executor used by the desktop host.</summary>
public static class ZMachineReplayRunner
{
    public static async Task<ZMachineReplayResult> RunAsync(
        StoryFile story,
        ZMachineReplayRoute route,
        BlorbFile? blorb = null,
        Action<ZMachineOpcodeTraceEntry>? traceWriter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(route);
        route.Validate();

        var state = ZMachineExecutionState.Start(
            story,
            pictureCatalog: blorb?.Pictures,
            soundCatalog: blorb?.Sounds,
            deterministicRandomSeed: route.RandomSeed);
        var input = new ReplayInputProvider(route.Commands, route.KeyPresses);
        var output = new StringBuilder();
        var trace = new OpcodeTraceCollector();
        var morePromptsAcknowledged = 0L;
        state.OutputStreams.ScreenWriter = text => output.Append(text);
        state.OutputStreams.Screen.OperationWriter = operation =>
        {
            if (operation.Kind == ZMachineScreenOperationKind.MorePrompt)
            {
                morePromptsAcknowledged++;
            }
        };

        var decoder = new ZMachineInstructionDecoder(state.Memory);
        var executor = new ZMachineOpcodeExecutor();
        var completion = ZMachineReplayCompletionStatus.Halted;
        string? error = null;

        while (!state.IsHalted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (trace.InstructionsExecuted >= route.MaximumInstructions)
            {
                completion = ZMachineReplayCompletionStatus.InstructionLimit;
                break;
            }

            try
            {
                var instruction = decoder.Decode(state.ProgramCounter);
                await executor.ExecuteAsync(instruction, state, input, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var entry = trace.Record(instruction, state);
                traceWriter?.Invoke(entry);
                await state.OutputStreams.Screen.WaitForMoreAsync(input.ReadMoreKeyAsync, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ReplayInputExhaustedException)
            {
                completion = ZMachineReplayCompletionStatus.AwaitingInput;
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                completion = ZMachineReplayCompletionStatus.Faulted;
                error = $"{exception.GetType().Name}: {exception.Message}";
                break;
            }
        }

        var text = output.ToString();
        var finalState = QuetzalCodec.Encode(state.CaptureSaveState(), story);
        var failure = ResolveRouteFailure(route, completion, text);
        return new ZMachineReplayResult(
            completion,
            story.Header.Version,
            story.Header.ReleaseNumber,
            story.Header.SerialNumber,
            Sha256(story.Bytes.Span),
            input.CommandsConsumed,
            input.KeyPressesConsumed,
            morePromptsAcknowledged,
            trace.InstructionsExecuted,
            FormatProgramCounter(state.ProgramCounter),
            Sha256(finalState),
            text,
            Sha256(Encoding.UTF8.GetBytes(text)),
            error,
            failure is null,
            failure,
            trace.Inventory);
    }

    private static string? ResolveRouteFailure(
        ZMachineReplayRoute route,
        ZMachineReplayCompletionStatus actualStatus,
        string output)
    {
        if (route.ExpectedCompletionStatus is { } expectedStatus)
        {
            if (!Enum.TryParse<ZMachineReplayCompletionStatus>(expectedStatus, ignoreCase: true, out var expected))
            {
                return $"Unsupported expected completion status '{expectedStatus}'.";
            }

            if (expected != actualStatus)
            {
                return $"Expected completion status {expected}, but replay ended with {actualStatus}.";
            }
        }

        if (route.ExpectedOutputSha256 is { } expectedHash
            && !string.Equals(expectedHash, Sha256(Encoding.UTF8.GetBytes(output)), StringComparison.OrdinalIgnoreCase))
        {
            return "Replay output SHA-256 did not match ExpectedOutputSha256.";
        }

        if (actualStatus is ZMachineReplayCompletionStatus.Faulted or ZMachineReplayCompletionStatus.InstructionLimit)
        {
            return $"Replay ended with {actualStatus}.";
        }

        return null;
    }

    internal static string FormatProgramCounter(uint address) => $"0x{address:X6}";

    private static string Sha256(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private sealed class ReplayInputProvider(string[] commands, ushort[] keyPresses) : IZMachineInputProvider
    {
        private int _commandIndex;
        private int _keyPressIndex;

        public bool SupportsTimedInput => false;

        public long CommandsConsumed => _commandIndex;

        public long KeyPressesConsumed => _keyPressIndex;

        public ValueTask<ZMachineLineInputResult> ReadLineAsync(
            ZMachineLineInputRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_commandIndex >= commands.Length)
            {
                throw new ReplayInputExhaustedException();
            }

            return ValueTask.FromResult(new ZMachineLineInputResult(commands[_commandIndex++], 13));
        }

        public ValueTask<ushort> ReadCharacterAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_keyPressIndex >= keyPresses.Length)
            {
                throw new ReplayInputExhaustedException();
            }

            return ValueTask.FromResult(keyPresses[_keyPressIndex++]);
        }

        public ValueTask<ushort> ReadMoreKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult((ushort)32);
        }
    }

    private sealed class ReplayInputExhaustedException : Exception
    {
    }

    private sealed class OpcodeTraceCollector
    {
        private readonly Dictionary<(InstructionForm Form, byte Opcode, string Mnemonic), InventoryCounter> _counts = [];

        public long InstructionsExecuted { get; private set; }

        public ImmutableArray<ZMachineOpcodeInventoryEntry> Inventory => _counts
            .Select(pair => new ZMachineOpcodeInventoryEntry(
                pair.Key.Form,
                pair.Key.Opcode,
                pair.Key.Mnemonic,
                pair.Value.Count,
                pair.Value.FirstSequence,
                pair.Value.FirstProgramCounter))
            .OrderBy(item => item.FirstSequence)
            .ToImmutableArray();

        public ZMachineOpcodeTraceEntry Record(DecodedInstruction instruction, ZMachineExecutionState state)
        {
            var sequence = ++InstructionsExecuted;
            var programCounter = FormatProgramCounter(instruction.ProgramCounter);
            var key = (instruction.Form, instruction.Opcode, instruction.Definition.Mnemonic);
            if (_counts.TryGetValue(key, out var count))
            {
                count.Count++;
            }
            else
            {
                _counts.Add(key, new InventoryCounter(sequence, programCounter));
            }

            return new ZMachineOpcodeTraceEntry(
                sequence,
                programCounter,
                instruction.Form,
                instruction.Class,
                instruction.Opcode,
                instruction.Definition.Mnemonic,
                instruction.Operands,
                instruction.StoreVariable,
                instruction.Branch,
                FormatProgramCounter(instruction.NextProgramCounter),
                FormatProgramCounter(state.ProgramCounter),
                state.IsHalted);
        }

        private sealed class InventoryCounter(long firstSequence, string firstProgramCounter)
        {
            public long Count { get; set; } = 1;

            public long FirstSequence { get; } = firstSequence;

            public string FirstProgramCounter { get; } = firstProgramCounter;
        }
    }
}
