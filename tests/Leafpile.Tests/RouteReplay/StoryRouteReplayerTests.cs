using Leafpile.Core.Loading;
using Leafpile.Core.Machine;
using Leafpile.RouteReplay;

namespace Leafpile.Tests.RouteReplay;

public sealed class StoryRouteReplayerTests
{
    [Fact]
    public void RouteCanBeSavedAndLoaded()
    {
        var story = StoryLoader.Load(CreateReadAndQuitStory());
        var route = CreateRoute(story, ["look"]) with { ScoreGlobal = null, MovesGlobal = null, LocationGlobalContainsActor = true };
        var path = Path.Combine(Path.GetTempPath(), $"leafpile-route-{Guid.NewGuid():N}.json");
        try
        {
            route.Save(path);
            var loaded = StoryRoute.Load(path);
            Assert.Equal(route.Name, loaded.Name);
            Assert.Equal(route.Story, loaded.Story);
            Assert.Equal(route.Commands, loaded.Commands);
            Assert.Null(loaded.ScoreGlobal);
            Assert.Null(loaded.MovesGlobal);
            Assert.True(loaded.LocationGlobalContainsActor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplayChecksIdentityCommandsScoreCheckpointsAndTrace()
    {
        var story = StoryLoader.Load(CreateReadAndQuitStory(score: 123));
        var route = CreateRoute(story, ["look"],
        [
            new RouteCheckpoint(1, Location: "Hall", Score: 123, ContainsText: "look", Halted: true)
        ]);

        var result = StoryRouteReplayer.Replay(story, route);

        Assert.Equal(1, result.CommandsReplayed);
        Assert.True(result.Halted);
        Assert.Equal(123, result.Score);
        Assert.NotEmpty(result.Trace);
        Assert.Contains("Variable.04 read", result.UniqueOpcodes);
        Assert.Equal(2, result.UniqueOpcodes.Count);
        Assert.All(result.Trace, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Form)));
        Assert.Equal(Enumerable.Range(1, result.InstructionsExecuted), result.Trace.Select(entry => entry.Sequence));
        Assert.Equal(result.InstructionsExecuted, result.OpcodeInventory.Sum(entry => entry.Count));
        Assert.Equal(result.Trace[0].Form, result.OpcodeInventory[0].Form);
        Assert.Equal(result.Trace[0].Sequence, result.OpcodeInventory[0].FirstSequence);
        Assert.Equal(result.Trace[0].Address, result.OpcodeInventory[0].FirstAddress);
        Assert.Equal(1, result.OpcodeInventory[0].Count);
        Assert.Equal(1, result.OpcodeInventory[1].Count);
    }

    [Fact]
    public void OpcodeInventoryPreservesActualFormAndFirstExecutionForRepeatedInstructions()
    {
        var image = CreateReadAndQuitStory();
        // Two VARIABLE-form read instructions followed by a SHORT-form quit.
        new byte[] { 0xe4, 0x0f, 0x01, 0x00, 0x01, 0x50, 0xe4, 0x0f, 0x01, 0x00, 0x01, 0x50, 0xba }
            .CopyTo(image, 0x200);
        var story = StoryLoader.Load(image);
        var result = StoryRouteReplayer.Replay(story, CreateRoute(story, ["look", "look"]));

        Assert.Equal(3, result.InstructionsExecuted);
        Assert.Equal(new[] { "Variable", "Variable", "Short" }, result.Trace.Select(entry => entry.Form));
        Assert.Equal(new[] { 1, 2, 3 }, result.Trace.Select(entry => entry.Sequence));
        Assert.Equal(2, result.OpcodeInventory.Count);
        Assert.Equal(new OpcodeExecutionSummary("Variable", 4, "read", 2, 1, 0x200), result.OpcodeInventory[0]);
        Assert.Equal(new OpcodeExecutionSummary("Short", 10, "quit", 1, 3, 0x20c), result.OpcodeInventory[1]);
        Assert.Equal(result.InstructionsExecuted, result.OpcodeInventory.Sum(entry => entry.Count));
    }

    [Fact]
    public void ReplayProvidesAnInMemoryQuetzalFileForSaveCommands()
    {
        var image = CreateReadAndQuitStory();
        new byte[] { 0xb5, 0xc2, 0xba }.CopyTo(image, 0x206); // save, branch on success, quit
        var story = StoryLoader.Load(image);
        var route = CreateRoute(story, ["look"], [new RouteCheckpoint(1, Location: "Hall", Halted: true)]);

        var result = StoryRouteReplayer.Replay(story, route);

        Assert.True(result.Halted);
        Assert.Equal(1, result.CommandsReplayed);
        Assert.Contains("ZeroOperand.05 save", result.UniqueOpcodes);
        Assert.Contains("ZeroOperand.10 quit", result.UniqueOpcodes);
    }

    [Fact]
    public void ReplayCanUseActorParentAsLocationWithoutNumericScoreGlobal()
    {
        var image = CreateReadAndQuitStory();
        const int objectOne = 0x70 + 62;
        const int objectTwo = objectOne + 9;
        image[objectOne + 4] = 2; // global 16 points to actor 1; actor 1 is in room 2.
        WriteWord(image, objectTwo + 7, 0x0190);
        var originalStory = StoryLoader.Load(image);
        var encoder = new ZMachineState(originalStory).Text;
        var roomName = encoder.EncodeZText("Room"u8);
        image[0x190] = (byte)(roomName.Length / 2);
        roomName.CopyTo(image, 0x191);
        var story = StoryLoader.Load(image);
        var state = new ZMachineState(story);
        Assert.Equal(2, new ZMachineObjectTable(state).GetParent(1));
        Assert.Equal("Room", new ZMachineObjectTable(state).GetShortName(2).Trim(), ignoreCase: true);
        var route = new StoryRoute("generated fixture", StoryIdentity.From(story), 31415, 1000, ["look"],
            [new RouteCheckpoint(1, Location: "Room", ContainsText: "look", Halted: true)],
            ScoreGlobal: null, MovesGlobal: null, LocationGlobalContainsActor: true);

        var result = StoryRouteReplayer.Replay(story, route);

        Assert.Null(result.Score);
        Assert.Null(result.Moves);
        Assert.Equal("Room", result.Location);
        Assert.Equal("Room", Assert.Single(result.Progress).Location);
        Assert.Null(Assert.Single(result.Progress).Score);
        Assert.Null(Assert.Single(result.Progress).Moves);
    }

    [Fact]
    public void ReplayPublishesProgressAsEachCommandCompletes()
    {
        var story = StoryLoader.Load(CreateReadAndQuitStory(score: 123));
        var route = CreateRoute(story, ["look"]);
        var observed = new List<RouteProgress>();

        var result = StoryRouteReplayer.Replay(story, route, progressObserver: observed.Add);

        Assert.Equal(result.Progress, observed);
        Assert.Equal(new[] { 1 }, observed.Select(item => item.CommandNumber));
        Assert.Equal(new[] { "look" }, observed.Select(item => item.Command));
    }

    [Fact]
    public void ReplaySuppliesSingleCharacterCommandsToReadCharAsKeypresses()
    {
        var story = StoryLoader.Load(CreateReadCharAndQuitStory());
        var route = CreateRoute(story, ["Y"], [new RouteCheckpoint(1, Location: "Hall", Halted: true)])
            with
        { ScoreGlobal = null, LocationGlobal = 17 };

        var result = StoryRouteReplayer.Replay(story, route);

        Assert.True(result.Halted);
        Assert.Contains("Variable.22 read_char", result.UniqueOpcodes);
        Assert.Contains("TwoOperand.01 je", result.UniqueOpcodes);
    }

    [Fact]
    public void ReplayDefersProgressCaptureUntilAfterStartupCharacterInput()
    {
        var image = CreateReadCharAndQuitStory();
        new byte[] { 0xf6, 0x7f, 0x01, 0x10, 0xe4, 0x0f, 0x01, 0x00, 0x01, 0x50, 0xba }
            .CopyTo(image, 0x200); // startup key, line input, quit
        image[0x100] = 16; // V4 text buffer capacity
        image[0x150] = 8; // V4 parse buffer capacity
        image[0x411] = 6; // V4 dictionary entry length
        var story = StoryLoader.Load(image);
        var route = CreateRoute(story, ["x", "look"],
            [new RouteCheckpoint(2, Location: "Hall", Halted: true)]) with
        { ScoreGlobal = null, LocationGlobal = 17 };

        var result = StoryRouteReplayer.Replay(story, route);

        Assert.True(result.Halted);
        Assert.Equal(2, result.CommandsReplayed);
        var progress = Assert.Single(result.Progress);
        Assert.Equal(2, progress.CommandNumber);
        Assert.Equal("look", progress.Command);
        Assert.Equal("Hall", progress.Location, ignoreCase: true);
    }

    [Fact]
    public void ReplayRejectsDifferentStoryIdentity()
    {
        var original = CreateReadAndQuitStory();
        var story = StoryLoader.Load(original);
        var otherImage = original.ToArray();
        otherImage[0x80] ^= 1;
        var otherStory = StoryLoader.Load(otherImage);

        var exception = Assert.Throws<InvalidDataException>(() =>
            StoryRouteReplayer.Replay(otherStory, CreateRoute(story, ["look"])));

        Assert.Contains("identity mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplayFailsWhenCheckpointDoesNotMatch()
    {
        var story = StoryLoader.Load(CreateReadAndQuitStory(score: 123));
        var route = CreateRoute(story, ["look"], [new RouteCheckpoint(1, Score: 124)]);

        var exception = Assert.Throws<InvalidOperationException>(() => StoryRouteReplayer.Replay(story, route));

        Assert.Contains("expected score 124, got 123", exception.Message);
    }

    private static StoryRoute CreateRoute(LoadedStory story, IReadOnlyList<string> commands, IReadOnlyList<RouteCheckpoint>? checkpoints = null) =>
        new("generated fixture", StoryIdentity.From(story), 31415, 1000, commands, checkpoints ?? []);

    private static byte[] CreateReadAndQuitStory(int score = 0)
    {
        const int length = 2048;
        var data = new byte[length];
        data[0] = 3;
        WriteWord(data, 0x04, 0x0300);
        WriteWord(data, 0x06, 0x0200);
        WriteWord(data, 0x08, 0x0310);
        WriteWord(data, 0x0a, 0x0070);
        WriteWord(data, 0x0c, 0x0040);
        WriteWord(data, 0x0e, 0x0300);
        WriteWord(data, 0x18, 0x0100);
        WriteWord(data, 0x1a, length / 2);
        WriteWord(data, 0x1c, 0x1234);
        WriteWord(data, 0x42, (ushort)score);
        WriteWord(data, 0x40, 1);
        const int firstObjectEntry = 0x70 + 62;
        WriteWord(data, firstObjectEntry + 7, 0x0180);
        data[firstObjectEntry + 9] = 0;
        data[0x180] = 1;
        WriteWord(data, 0x181, 0x34d1); // "Hall"
        WriteWord(data, 0x183, 0xc400);
        data[0x100] = 16;
        data[0x150] = 4;
        data[0x310] = 0;
        data[0x311] = 4;
        data[0x312] = 0;
        data[0x313] = 0;
        new byte[] { 0xe4, 0x0f, 0x01, 0x00, 0x01, 0x50, 0xba }.CopyTo(data, 0x200);
        return data;
    }

    private static byte[] CreateReadCharAndQuitStory()
    {
        const int length = 4096;
        const int objectTable = 0x70;
        const int firstObject = objectTable + 126;
        const int roomObject = firstObject + 14;
        var data = new byte[length];
        data[0] = 4;
        WriteWord(data, 0x04, 0x0400);
        WriteWord(data, 0x06, 0x0200);
        WriteWord(data, 0x08, 0x0410);
        WriteWord(data, 0x0a, objectTable);
        WriteWord(data, 0x0c, 0x0040);
        WriteWord(data, 0x0e, 0x0400);
        WriteWord(data, 0x18, 0x0100);
        WriteWord(data, 0x1a, length / 4);
        WriteWord(data, 0x1c, 0x1234);
        WriteWord(data, 0x42, 2); // Global 17 names room object 2.
        WriteWord(data, roomObject + 12, 0x0190);
        data[0x190] = 1;
        WriteWord(data, 0x191, 0x34d1); // "Hall"
        WriteWord(data, 0x193, 0xc400);
        data[0x410] = 0;
        data[0x411] = 4;
        data[0x412] = 0;
        data[0x413] = 0;
        new byte[]
        {
            0xf6, 0x7f, 0x01, 0x10, // read_char 1 -> global 0
            0x41, 0x10, 0x59, 0xc6, // if global 0 == 'Y', skip the retry
            0xf6, 0x7f, 0x01, 0x10, // a wrong character requests another route key
            0xba
        }.CopyTo(data, 0x200);
        return data;
    }

    private static void WriteWord(byte[] data, int address, int value)
    {
        data[address] = (byte)(value >> 8);
        data[address + 1] = (byte)value;
    }
}
