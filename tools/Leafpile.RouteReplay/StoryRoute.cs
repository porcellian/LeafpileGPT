using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leafpile.Core.Loading;
using Leafpile.Core.Machine;

namespace Leafpile.RouteReplay;

public sealed record StoryIdentity(byte Version, ushort Release, string Serial, ushort Checksum, string Sha256)
{
    public static StoryIdentity From(LoadedStory story) => new(
        story.Header.Version,
        story.Header.ReleaseNumber,
        story.Header.SerialNumber,
        story.Header.Checksum,
        Convert.ToHexString(SHA256.HashData(story.Data.Span)).ToLowerInvariant());
}

public sealed record RouteCheckpoint(
    int CommandNumber,
    string? Location = null,
    int? Score = null,
    string? ContainsText = null,
    bool? Halted = null);

public sealed record StoryRoute(
    string Name,
    StoryIdentity Story,
    int RandomSeed,
    int MaximumInstructions,
    IReadOnlyList<string> Commands,
    IReadOnlyList<RouteCheckpoint> Checkpoints,
    int LocationGlobal = 16,
    int ScoreGlobal = 17,
    int MovesGlobal = 18)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static StoryRoute Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return JsonSerializer.Deserialize<StoryRoute>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Route file '{path}' is empty.");
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);
    }
}

public sealed record InstructionTraceEntry(
    int CommandNumber,
    int Sequence,
    int Address,
    string Form,
    string OpcodeClass,
    int Opcode,
    string Mnemonic);

public sealed record OpcodeExecutionSummary(
    string Form,
    int Opcode,
    string Mnemonic,
    int Count,
    int FirstSequence,
    int FirstAddress);

public sealed record RouteReplayResult(
    int CommandsReplayed,
    int InstructionsExecuted,
    int Score,
    int Moves,
    string Location,
    bool Halted,
    IReadOnlyList<InstructionTraceEntry> Trace,
    IReadOnlyList<RouteProgress> Progress)
{
    public IReadOnlyList<OpcodeExecutionSummary> OpcodeInventory => Trace
        .GroupBy(entry => new { entry.Form, entry.Opcode, entry.Mnemonic })
        .Select(group => new OpcodeExecutionSummary(
            group.Key.Form,
            group.Key.Opcode,
            group.Key.Mnemonic,
            group.Count(),
            group.Min(entry => entry.Sequence),
            group.OrderBy(entry => entry.Sequence).First().Address))
        .OrderBy(entry => entry.FirstSequence)
        .ToArray();

    public IReadOnlyList<string> UniqueOpcodes => Trace
        .Select(entry => $"{entry.OpcodeClass}.{entry.Opcode:00} {entry.Mnemonic}")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

public sealed record RouteProgress(int CommandNumber, string Command, string Location, int Score, int Moves, string ScreenText);

public static class StoryRouteReplayer
{
    public static RouteReplayResult Replay(LoadedStory story, StoryRoute route, int? instructionLimit = null)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(route);
        var actualIdentity = StoryIdentity.From(story);
        if (actualIdentity != route.Story)
        {
            throw new InvalidDataException($"Story identity mismatch for route '{route.Name}'. Expected V{route.Story.Version} release {route.Story.Release}, serial {route.Story.Serial}, checksum {route.Story.Checksum:x4}, SHA-256 {route.Story.Sha256}; got V{actualIdentity.Version} release {actualIdentity.Release}, serial {actualIdentity.Serial}, checksum {actualIdentity.Checksum:x4}, SHA-256 {actualIdentity.Sha256}.");
        }

        if (route.Commands.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("A route cannot contain empty commands.");
        if (route.RandomSeed is < 1 or > 32767) throw new InvalidDataException("A deterministic route seed must be between 1 and 32767.");
        if (route.MaximumInstructions < 1) throw new InvalidDataException("The route instruction limit must be positive.");

        var state = new ZMachineState(story);
        state.Random.UseDeterministicSeed(route.RandomSeed);
        var host = new RouteHost(state, route);
        var saveRestore = new QuetzalSaveRestoreService(host);
        var trace = new List<InstructionTraceEntry>();
        var sequence = 0;
        var instructionCount = 0;
        var executor = new ZMachineOpcodeExecutor(state, saveRestore: saveRestore, inputOutput: host, instructionObserver: instruction =>
        {
            trace.Add(new InstructionTraceEntry(host.LastCommandNumber, ++sequence, instruction.Address,
                instruction.Form.ToString(),
                instruction.OpcodeClass.ToString(), instruction.Opcode,
                OpcodeNames.Get(instruction.OpcodeClass, instruction.Opcode)));
        });

        var halted = false;
        var limit = Math.Min(route.MaximumInstructions, instructionLimit ?? route.MaximumInstructions);
        while (!halted)
        {
            if (instructionCount >= limit) throw new InvalidOperationException($"Route exceeded its {limit} instruction limit at PC 0x{state.ProgramCounter:x4}.");
            var execution = executor.ExecuteNext();
            instructionCount++;
            halted = execution.Halted;
            if (halted) host.ValidateCheckpoint(route.Commands.Count, halted);
        }

        if (host.CommandsConsumed != route.Commands.Count)
        {
            throw new InvalidOperationException($"Story halted after {host.CommandsConsumed} of {route.Commands.Count} route commands.");
        }

        var finalScore = ReadGlobal(state, route.ScoreGlobal);
        var moves = ReadGlobal(state, route.MovesGlobal);
        var location = ReadLocation(state, route.LocationGlobal);
        return new RouteReplayResult(host.CommandsConsumed, instructionCount, finalScore, moves, location, halted, trace, host.Progress);
    }

    private static int ReadGlobal(ZMachineState state, int variable)
    {
        if (variable is < 16 or > 255) throw new InvalidDataException($"Global variable number {variable} is invalid.");
        return (short)state.ReadVariableValue((byte)variable);
    }

    private static string ReadLocation(ZMachineState state, int variable)
    {
        if (variable is < 16 or > 255) throw new InvalidDataException($"Global variable number {variable} is invalid.");
        var objectNumber = state.ReadVariableValue((byte)variable);
        return objectNumber == 0 ? string.Empty : new ZMachineObjectTable(state).GetShortName(objectNumber).Trim();
    }

    private sealed class RouteHost(ZMachineState state, StoryRoute route) : IZMachineInputOutput, IZMachineSaveFileAccess
    {
        private int _nextCommand;
        private int _lastValidatedCommand = -1;
        private readonly List<RouteProgress> _progress = [];
        private byte[]? _saveFile;
        public int LastCommandNumber { get; private set; } = -1;
        public int CommandsConsumed => _nextCommand;
        public IReadOnlyList<RouteProgress> Progress => _progress;
        public bool SupportsTimedInput => false;
        public bool SupportsCommandInput => false;
        public bool SupportsTranscript => false;
        public bool SupportsCommandRecording => false;

        public bool WriteSaveFile(ReadOnlyMemory<byte> saveFile)
        {
            _saveFile = saveFile.ToArray();
            return true;
        }

        public ReadOnlyMemory<byte>? ReadSaveFile() => _saveFile;

        public ZMachineInputResponse ReadInput(ZMachineInputRequest request, Func<bool>? timedInterrupt)
        {
            if (_nextCommand > 0)
            {
                ValidateCheckpoint(_nextCommand);
            }
            if (_nextCommand >= route.Commands.Count) throw new InvalidOperationException("Story requested another command after the route ended.");
            LastCommandNumber = _nextCommand;
            var command = route.Commands[_nextCommand++];
            return new ZMachineInputResponse(command, 13);
        }

        public void ValidateCheckpoint(int commandCount, bool halted = false)
        {
            var commandNumber = commandCount - 1;
            if (commandNumber < 0 || commandNumber >= route.Commands.Count || commandNumber <= _lastValidatedCommand) return;
            _lastValidatedCommand = commandNumber;
            RecordProgress(commandNumber);
            var checkpoint = route.Checkpoints.SingleOrDefault(item => item.CommandNumber == commandNumber + 1);
            if (checkpoint is null) return;
            var location = ReadLocation(state, route.LocationGlobal);
            var score = ReadGlobal(state, route.ScoreGlobal);
            var moves = ReadGlobal(state, route.MovesGlobal);
            var lowerText = state.Screen.LowerWindowText;
            if (checkpoint.Location is not null && !string.Equals(location, checkpoint.Location, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Checkpoint {commandNumber + 1} expected location '{checkpoint.Location}', got '{location}'.");
            if (checkpoint.Score.HasValue && score != checkpoint.Score.Value)
                throw new InvalidOperationException($"Checkpoint {commandNumber + 1} expected score {checkpoint.Score.Value}, got {score}.");
            if (checkpoint.ContainsText is not null && !lowerText.Contains(checkpoint.ContainsText, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Checkpoint {commandNumber + 1} expected output containing '{checkpoint.ContainsText}'.");
            if (checkpoint.Halted.HasValue && checkpoint.Halted.Value != halted)
                throw new InvalidOperationException($"Checkpoint {commandNumber + 1} expected halted={checkpoint.Halted.Value}, got {halted}.");
        }

        private void RecordProgress(int commandIndex)
        {
            if (commandIndex < 0 || commandIndex >= route.Commands.Count || _progress.Any(item => item.CommandNumber == commandIndex + 1)) return;
            _progress.Add(new RouteProgress(commandIndex + 1, route.Commands[commandIndex],
                ReadLocation(state, route.LocationGlobal), ReadGlobal(state, route.ScoreGlobal), ReadGlobal(state, route.MovesGlobal), state.Screen.LowerWindowText));
        }

        public void WriteScreenOutput(string text) { }
        public void WriteTranscript(string text) { }
        public void WriteCommand(string text) { }
        public void WriteKeypress(byte zsciiCode) { }
        public void ReportFileAccessUnavailable(string feature) { }
    }
}

public static class OpcodeNames
{
    private static readonly IReadOnlyDictionary<ZMachineOpcodeClass, string[]> Names = new Dictionary<ZMachineOpcodeClass, string[]>
    {
        [ZMachineOpcodeClass.TwoOperand] = ["", "je", "jl", "jg", "dec_chk", "inc_chk", "jin", "test", "or", "and", "test_attr", "set_attr", "clear_attr", "store", "insert_obj", "loadw", "loadb", "get_prop", "get_prop_addr", "get_next_prop", "add", "sub", "mul", "div", "mod", "call_2s", "call_2n", "set_colour", "throw"],
        [ZMachineOpcodeClass.OneOperand] = ["jz", "get_sibling", "get_child", "get_parent", "get_prop_len", "inc", "dec", "print_addr", "call_1s", "remove_obj", "print_obj", "ret", "jump", "print_paddr", "load", "not"],
        [ZMachineOpcodeClass.ZeroOperand] = ["rtrue", "rfalse", "print", "print_ret", "nop", "save", "restore", "restart", "ret_popped", "pop", "quit", "new_line", "show_status", "verify", "", "piracy"],
        [ZMachineOpcodeClass.Variable] = ["call_vs", "storew", "storeb", "put_prop", "read", "print_char", "print_num", "random", "push", "pull", "split_window", "set_window", "call_vs2", "erase_window", "erase_line", "set_cursor", "get_cursor", "set_text_style", "buffer_mode", "output_stream", "input_stream", "sound_effect", "read_char", "scan_table", "not", "call_vn", "call_vn2", "tokenise", "encode_text", "copy_table", "print_table", "check_arg_count"],
        [ZMachineOpcodeClass.Extended] = ["save", "restore", "log_shift", "art_shift", "set_font", "draw_picture", "picture_data", "erase_picture", "set_margins", "save_undo", "restore_undo", "print_unicode", "check_unicode", "set_true_colour", "move_window", "window_size", "window_style", "get_wind_prop", "scroll_window", "pop_stack", "read_mouse", "mouse_window", "push_stack", "put_wind_prop", "print_form", "make_menu", "picture_table"]
    };

    public static string Get(ZMachineOpcodeClass opcodeClass, int opcode) =>
        Names.TryGetValue(opcodeClass, out var names) && opcode >= 0 && opcode < names.Length && names[opcode].Length > 0
            ? names[opcode]
            : "unknown";
}
