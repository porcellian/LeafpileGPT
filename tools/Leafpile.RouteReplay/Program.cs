using System.Text.Json;
using Leafpile.Core.Loading;
using Leafpile.RouteReplay;

if (args.Length < 2 || args.Length > 4)
{
    Console.Error.WriteLine("Usage: Leafpile.RouteReplay <story-file> <route.json> [trace.jsonl] [--progress]");
    return 2;
}

string? tracePathArgument = null;
var showProgress = false;
foreach (var argument in args.Skip(2))
{
    if (argument == "--progress")
    {
        showProgress = true;
    }
    else if (argument.StartsWith("--", StringComparison.Ordinal) || tracePathArgument is not null)
    {
        Console.Error.WriteLine("Usage: Leafpile.RouteReplay <story-file> <route.json> [trace.jsonl] [--progress]");
        return 2;
    }
    else
    {
        tracePathArgument = argument;
    }
}

try
{
    var story = StoryLoader.LoadFile(args[0]);
    var route = StoryRoute.Load(args[1]);
    var result = StoryRouteReplayer.Replay(story, route, progressObserver: showProgress ? PrintProgress : null);
    if (tracePathArgument is not null)
    {
        var tracePath = Path.GetFullPath(tracePathArgument);
        Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
        await using var writer = new StreamWriter(tracePath, false);
        foreach (var instruction in result.Trace)
            await writer.WriteLineAsync(JsonSerializer.Serialize(instruction));
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            instructionsExecuted = result.InstructionsExecuted,
            opcodeInventory = result.OpcodeInventory
        }));
    }

    var score = result.Score?.ToString() ?? "n/a";
    Console.WriteLine($"Route '{route.Name}' passed: {result.CommandsReplayed} commands, {result.InstructionsExecuted} instructions, score {score}, moves {result.Moves}, location '{result.Location}', halted={result.Halted}.");
    Console.WriteLine($"Unique opcodes: {result.OpcodeInventory.Count}; total instructions: {result.InstructionsExecuted}");
    foreach (var opcode in result.OpcodeInventory)
        Console.WriteLine($"{opcode.Form,-8} {opcode.Opcode,2} {opcode.Mnemonic,-20} {opcode.Count,8} {opcode.FirstSequence,10} 0x{opcode.FirstAddress:x4}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Route replay failed: {exception.Message}");
    return 1;
}

static void PrintProgress(RouteProgress progress)
{
    var score = progress.Score?.ToString() ?? "n/a";
    Console.WriteLine($"{progress.CommandNumber,3}: {progress.Command,-32} | {progress.Location,-24} | {score,3} points | {progress.Moves,3} moves");
    Console.WriteLine(progress.ScreenText.Replace("\n", " "));
}
