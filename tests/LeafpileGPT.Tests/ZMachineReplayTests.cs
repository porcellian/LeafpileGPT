using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class ZMachineReplayTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ReplayRunsGeneratedQuitStoryAndTracesOpcodeIdentity(byte version)
    {
        var story = CreateExecutableStory(version, [0xBA]);
        var route = new ZMachineReplayRoute { StoryPath = "generated.z" };
        var trace = new List<ZMachineOpcodeTraceEntry>();

        var result = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            traceWriter: trace.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ZMachineReplayCompletionStatus.Halted, result.CompletionStatus);
        Assert.True(result.RoutePassed);
        Assert.Equal(1, result.InstructionsExecuted);
        Assert.Single(trace);
        Assert.Equal(1, trace[0].Sequence);
        Assert.Equal("quit", trace[0].Mnemonic);
        Assert.Equal(version == 6 ? "0x000321" : "0x000352", trace[0].ProgramCounter);
        var inventory = Assert.Single(result.OpcodeInventory);
        Assert.Equal(InstructionForm.Short, inventory.Form);
        Assert.Equal((byte)10, inventory.Opcode);
        Assert.Equal("quit", inventory.Mnemonic);
        Assert.Equal(1, inventory.Count);
        Assert.Equal(1, inventory.FirstSequence);
        Assert.Equal(trace[0].ProgramCounter, inventory.FirstProgramCounter);
    }

    [Fact]
    public async Task TraceDoesNotChangeReplayOutputOrFinalVmState()
    {
        var story = CreateExecutableStory(5, [0xE5, 0x7F, (byte)'A', 0xE5, 0x7F, (byte)'B', 0xBA]);
        var route = new ZMachineReplayRoute
        {
            StoryPath = "generated.z",
            ExpectedOutputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AB")))
        };
        var tracedEntries = new List<ZMachineOpcodeTraceEntry>();

        var withoutTrace = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            cancellationToken: TestContext.Current.CancellationToken);
        var withTrace = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            traceWriter: tracedEntries.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("AB", withoutTrace.Output);
        Assert.Equal(withoutTrace.Output, withTrace.Output);
        Assert.Equal(withoutTrace.FinalProgramCounter, withTrace.FinalProgramCounter);
        Assert.Equal(withoutTrace.FinalStateSha256, withTrace.FinalStateSha256);
        Assert.Equal(withoutTrace.OpcodeInventory, withTrace.OpcodeInventory);
        Assert.Equal(3, tracedEntries.Count);
        Assert.Equal(new[] { "print_char", "print_char", "quit" }, tracedEntries.Select(entry => entry.Mnemonic));
        Assert.Equal(new long[] { 1, 2, 3 }, tracedEntries.Select(entry => entry.Sequence));
        Assert.Equal("0x000352", tracedEntries[0].ProgramCounter);
        Assert.Equal("0x000355", tracedEntries[1].ProgramCounter);
        Assert.Equal(2, withoutTrace.OpcodeInventory[0].Count);
        Assert.Equal(1, withoutTrace.OpcodeInventory[0].FirstSequence);
    }

    [Fact]
    public async Task RouteFeedsCommandsAndStopsAfterTheStoryHalts()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        WriteCode(bytes, 850, [0xE4, 0x5F, 220, 0, 16, 0xBA]);
        bytes[220] = 16;
        bytes[221] = 0;
        RecalculateChecksum(bytes);
        var story = StoryFile.Parse(bytes);
        var route = new ZMachineReplayRoute { StoryPath = "story with spaces.z5", Commands = ["look"] };

        var result = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ZMachineReplayCompletionStatus.Halted, result.CompletionStatus);
        Assert.Equal(1, result.CommandsConsumed);
        Assert.Equal(2, result.InstructionsExecuted);
        Assert.Equal(new[] { "aread", "quit" }, result.OpcodeInventory.Select(entry => entry.Mnemonic));
    }

    [Fact]
    public async Task RouteReportsAwaitingInputWithoutInventingACommand()
    {
        var bytes = StoryFixtureBuilder.Create(5);
        WriteCode(bytes, 850, [0xE4, 0x5F, 220, 0, 16, 0xBA]);
        bytes[220] = 16;
        bytes[221] = 0;
        RecalculateChecksum(bytes);
        var story = StoryFile.Parse(bytes);
        var route = new ZMachineReplayRoute { StoryPath = "story.z5" };

        var result = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ZMachineReplayCompletionStatus.AwaitingInput, result.CompletionStatus);
        Assert.Equal(0, result.CommandsConsumed);
        Assert.Equal(0, result.InstructionsExecuted);
        Assert.Equal("0x000352", result.FinalProgramCounter);
    }

    [Fact]
    public async Task ReplayHonorsInstructionLimitAndExpectedResultChecks()
    {
        var story = CreateExecutableStory(5, [0xE5, 0x7F, (byte)'A', 0xBA]);
        var route = new ZMachineReplayRoute
        {
            StoryPath = "generated.z",
            MaximumInstructions = 1,
            ExpectedCompletionStatus = nameof(ZMachineReplayCompletionStatus.Halted)
        };

        var result = await ZMachineReplayRunner.RunAsync(
            story,
            route,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ZMachineReplayCompletionStatus.InstructionLimit, result.CompletionStatus);
        Assert.False(result.RoutePassed);
        Assert.Contains("Expected completion status Halted", result.RouteFailure);
    }

    [Fact]
    public async Task ReplayRandomSequenceIsStableForTheSameSeedAndResetsOnRestart()
    {
        var story = CreateExecutableStory(5, [0xBA]);
        var first = ZMachineExecutionState.Start(story, deterministicRandomSeed: 1234);
        var second = ZMachineExecutionState.Start(story, deterministicRandomSeed: 1234);
        var firstSequence = Enumerable.Range(0, 12).Select(_ => first.GetRandomNumber(1000)).ToArray();
        var secondSequence = Enumerable.Range(0, 12).Select(_ => second.GetRandomNumber(1000)).ToArray();

        Assert.Equal(firstSequence, secondSequence);

        first.Restart();

        Assert.Equal(firstSequence, Enumerable.Range(0, 12).Select(_ => first.GetRandomNumber(1000)));
    }

    [Fact]
    public void ReplayProjectDoesNotReferenceAvalonia()
    {
        var replayAssembly = typeof(ZMachineReplayRunner).Assembly;

        Assert.DoesNotContain(
            replayAssembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RouteResolvesRelativeStoryPathsFromTheRouteDirectory()
    {
        var route = new ZMachineReplayRoute { StoryPath = "../stories/story with spaces.z5" };
        var routePath = Path.Combine(Path.GetTempPath(), "routes", "intro.json");

        var storyPath = route.ResolveStoryPath(routePath);

        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "stories", "story with spaces.z5")), storyPath);
    }

    [Fact]
    public void RouteRejectsUnsupportedVersionsMalformedHashesAndMissingStoryPaths()
    {
        Assert.Throws<InvalidDataException>(() => new ZMachineReplayRoute { SchemaVersion = 2, StoryPath = "story.z3" }.Validate());
        Assert.Throws<InvalidDataException>(() => new ZMachineReplayRoute { StoryPath = "story.z3", ExpectedOutputSha256 = "bad" }.Validate());
        Assert.Throws<ArgumentException>(() => new ZMachineReplayRoute().Validate());
    }

    [Fact]
    public void RouteFileLoadsCaseInsensitiveJsonAndValidatesItsSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"route-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"storyPath\":\"story with spaces.z3\",\"commands\":[\"look\"]}");

            var route = ZMachineReplayRoute.Load(path);

            Assert.Equal("story with spaces.z3", route.StoryPath);
            Assert.Equal(new[] { "look" }, route.Commands);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReplayCliHandlesStoryAndRoutePathsWithSpacesAndWritesMachineTrace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay cli {Guid.NewGuid():N}");
        var storyDirectory = Path.Combine(root, "Story files");
        var routeDirectory = Path.Combine(root, "Route files");
        Directory.CreateDirectory(storyDirectory);
        Directory.CreateDirectory(routeDirectory);
        try
        {
            var storyBytes = StoryFixtureBuilder.Create(5);
            WriteCode(storyBytes, 850, [0xBA]);
            RecalculateChecksum(storyBytes);
            File.WriteAllBytes(Path.Combine(storyDirectory, "fixture story.z5"), storyBytes);
            File.WriteAllText(Path.Combine(routeDirectory, "test route.json"), """
                {
                  "schemaVersion": 1,
                  "storyPath": "../Story files/fixture story.z5",
                  "expectedCompletionStatus": "Halted"
                }
                """);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LeafpileGPT.Replay.dll"));
            process.StartInfo.ArgumentList.Add(Path.Combine(routeDirectory, "test route.json"));
            process.StartInfo.ArgumentList.Add("--trace");
            process.StartInfo.ArgumentList.Add("trace file.jsonl");
            Assert.True(process.Start());
            var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await standardError);
            using var result = JsonDocument.Parse(await standardOutput);
            Assert.Equal("Halted", result.RootElement.GetProperty("CompletionStatus").GetString());
            Assert.True(result.RootElement.GetProperty("RoutePassed").GetBoolean());
            var traceLines = File.ReadAllLines(Path.Combine(routeDirectory, "trace file.jsonl"));
            Assert.Single(traceLines);
            using var traceEntry = JsonDocument.Parse(traceLines[0]);
            Assert.Equal("quit", traceEntry.RootElement.GetProperty("Mnemonic").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReplayCliReportsMissingStoryAsMachineReadableFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay missing {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var routePath = Path.Combine(root, "missing story route.json");
        File.WriteAllText(routePath, "{\"schemaVersion\":1,\"storyPath\":\"missing story.z5\"}");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LeafpileGPT.Replay.dll"));
            process.StartInfo.ArgumentList.Add(routePath);
            Assert.True(process.Start());
            var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, process.ExitCode);
            using var error = JsonDocument.Parse(await standardError);
            Assert.Equal("InvalidRouteOrStory", error.RootElement.GetProperty("CompletionStatus").GetString());
            Assert.Contains("missing story.z5", error.RootElement.GetProperty("Error").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReplayCliRunsAllLocalRoutesAndReportsEachResultAsJsonl()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay routes {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            WriteRouteStory(root, "fixture-a.z5", 101);
            WriteRouteStory(root, "fixture-b.z5", 202);
            File.WriteAllText(Path.Combine(root, "route-b.json"), "{\"schemaVersion\":1,\"storyPath\":\"fixture-b.z5\"}");
            File.WriteAllText(Path.Combine(root, "route-a.json"), "{\"schemaVersion\":1,\"storyPath\":\"fixture-a.z5\"}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LeafpileGPT.Replay.dll"));
            process.StartInfo.ArgumentList.Add("--all");
            process.StartInfo.ArgumentList.Add(root);
            Assert.True(process.Start());
            var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await standardError);
            var results = (await standardOutput).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, results.Length);
            using var first = JsonDocument.Parse(results[0]);
            using var second = JsonDocument.Parse(results[1]);
            Assert.Equal("Halted", first.RootElement.GetProperty("CompletionStatus").GetString());
            Assert.True(first.RootElement.GetProperty("RoutePassed").GetBoolean());
            Assert.Equal(101, first.RootElement.GetProperty("ReleaseNumber").GetInt32());
            Assert.Equal("Halted", second.RootElement.GetProperty("CompletionStatus").GetString());
            Assert.True(second.RootElement.GetProperty("RoutePassed").GetBoolean());
            Assert.Equal(202, second.RootElement.GetProperty("ReleaseNumber").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static StoryFile CreateExecutableStory(byte version, byte[] code)
    {
        var bytes = StoryFixtureBuilder.Create(version);
        var start = version == 6 ? 801 : 850;
        if (version == 6)
        {
            bytes[800] = 0;
        }

        WriteCode(bytes, start, code);
        RecalculateChecksum(bytes);
        return StoryFile.Parse(bytes);
    }

    private static void WriteCode(byte[] story, int address, ReadOnlySpan<byte> code) => code.CopyTo(story.AsSpan(address));

    private static void RecalculateChecksum(byte[] story) =>
        StoryFixtureBuilder.WriteWord(story, 0x1C, StoryFixtureBuilder.CalculateChecksum(story));

    private static void WriteRouteStory(string directory, string filename, ushort releaseNumber)
    {
        var bytes = StoryFixtureBuilder.Create(5);
        StoryFixtureBuilder.WriteWord(bytes, 0x02, releaseNumber);
        WriteCode(bytes, 850, [0xBA]);
        RecalculateChecksum(bytes);
        File.WriteAllBytes(Path.Combine(directory, filename), bytes);
    }
}
