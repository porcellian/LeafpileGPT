using System.Text.Json;
using System.Text.Json.Serialization;
using LeafpileGPT.Core;

var jsonOptions = CreateJsonOptions();
var traceJsonOptions = CreateJsonOptions(indented: false);

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: LeafpileGPT.Replay <route.json> [--trace <trace.jsonl>]");
    Console.WriteLine("       LeafpileGPT.Replay --all <route-directory>");
    return 0;
}

if (args.Length == 2 && args[0] == "--all")
{
    try
    {
        var routeDirectory = Path.GetFullPath(args[1]);
        var routePaths = Directory.GetFiles(routeDirectory, "*.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (routePaths.Length == 0)
        {
            throw new InvalidDataException($"No replay route JSON files were found in '{routeDirectory}'.");
        }

        var everyRoutePassed = true;
        foreach (var routePath in routePaths)
        {
            try
            {
                var result = await ReplayRouteAsync(routePath, tracePath: null, traceJsonOptions);
                Console.WriteLine(JsonSerializer.Serialize(result, traceJsonOptions));
                everyRoutePassed &= result.RoutePassed;
            }
            catch (Exception exception) when (IsReplaySetupException(exception))
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    RoutePath = routePath,
                    CompletionStatus = "InvalidRouteOrStory",
                    Error = $"{exception.GetType().Name}: {exception.Message}"
                }, traceJsonOptions));
                everyRoutePassed = false;
            }
        }

        return everyRoutePassed ? 0 : 1;
    }
    catch (Exception exception) when (IsReplaySetupException(exception))
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            CompletionStatus = "InvalidRouteDirectory",
            Error = $"{exception.GetType().Name}: {exception.Message}"
        }, jsonOptions));
        return 2;
    }
}

if (args.Length is not 1 and not 3 || args.Length == 3 && args[1] != "--trace")
{
    Console.Error.WriteLine("Usage: LeafpileGPT.Replay <route.json> [--trace <trace.jsonl>]");
    Console.Error.WriteLine("       LeafpileGPT.Replay --all <route-directory>");
    return 2;
}

try
{
    var routePath = Path.GetFullPath(args[0]);
    var result = await ReplayRouteAsync(routePath, args.Length == 3 ? args[2] : null, traceJsonOptions);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return result.RoutePassed ? 0 : 1;
}
catch (Exception exception) when (IsReplaySetupException(exception))
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        CompletionStatus = "InvalidRouteOrStory",
        Error = $"{exception.GetType().Name}: {exception.Message}"
    }, jsonOptions));
    return 2;
}

static async Task<ZMachineReplayResult> ReplayRouteAsync(
    string routePath,
    string? tracePath,
    JsonSerializerOptions traceJsonOptions)
{
    var route = ZMachineReplayRoute.Load(routePath);
    var storyPath = route.ResolveStoryPath(routePath);
    BlorbFile? blorb = null;
    StoryFile story;
    if (Path.GetExtension(storyPath).Equals(".blorb", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(storyPath).Equals(".blb", StringComparison.OrdinalIgnoreCase))
    {
        blorb = BlorbFile.Load(storyPath);
        story = blorb.ZCodeStory
            ?? throw new InvalidDataException("The Blorb container has no supported Z-code executable.");
    }
    else
    {
        story = StoryFile.Load(storyPath);
    }

    using var trace = tracePath is null ? null : OpenTraceFile(tracePath, routePath);
    Action<ZMachineOpcodeTraceEntry>? traceWriter = trace is null
        ? null
        : entry => trace.WriteLine(JsonSerializer.Serialize(entry, traceJsonOptions));
    return await ZMachineReplayRunner.RunAsync(story, route, blorb, traceWriter);
}

static bool IsReplaySetupException(Exception exception) => exception is IOException or UnauthorizedAccessException
    or ArgumentException or InvalidDataException or StoryFileFormatException or BlorbFormatException
    or JsonException;

static StreamWriter OpenTraceFile(string path, string routePath)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    var routeDirectory = Path.GetDirectoryName(routePath)!;
    var tracePath = Path.GetFullPath(path, routeDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
    return new StreamWriter(tracePath, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static JsonSerializerOptions CreateJsonOptions(bool indented = true)
{
    var options = new JsonSerializerOptions { WriteIndented = indented };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}
