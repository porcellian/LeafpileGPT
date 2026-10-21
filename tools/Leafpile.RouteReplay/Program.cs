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
    var result = StoryRouteReplayer.Replay(story, route);
    if (tracePathArgument is not null)
    {
        var tracePath = Path.GetFullPath(tracePathArgument);
        Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
        await using var writer = new StreamWriter(tracePath, false);
        foreach (var instruction in result.Trace)
            await writer.WriteLineAsync(JsonSerializer.Serialize(instruction));
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { uniqueOpcodes = result.UniqueOpcodes }));
    }

    Console.WriteLine($"Route '{route.Name}' passed: {result.CommandsReplayed} commands, {result.InstructionsExecuted} instructions, score {result.Score}, moves {result.Moves}, location '{result.Location}', halted={result.Halted}.");
    if (showProgress)
    {
        foreach (var progress in result.Progress)
        {
            Console.WriteLine($"{progress.CommandNumber,3}: {progress.Command,-32} | {progress.Location,-24} | {progress.Score,3} points | {progress.Moves,3} moves");
            Console.WriteLine(progress.ScreenText.Replace("\n", " "));
        }
    }
    Console.WriteLine($"Unique opcodes: {result.UniqueOpcodes.Count}");
    foreach (var opcode in result.UniqueOpcodes) Console.WriteLine(opcode);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Route replay failed: {exception.Message}");
    return 1;
}
