using Leafpile.Core.Loading;
using Leafpile.RouteReplay;

namespace Leafpile.Tests.RouteReplay;

public sealed class StoryRouteReplayerTests
{
    [Fact]
    public void RouteCanBeSavedAndLoaded()
    {
        var story = StoryLoader.Load(CreateReadAndQuitStory());
        var route = CreateRoute(story, ["look"]);
        var path = Path.Combine(Path.GetTempPath(), $"leafpile-route-{Guid.NewGuid():N}.json");
        try
        {
            route.Save(path);
            var loaded = StoryRoute.Load(path);
            Assert.Equal(route.Name, loaded.Name);
            Assert.Equal(route.Story, loaded.Story);
            Assert.Equal(route.Commands, loaded.Commands);
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

    private static void WriteWord(byte[] data, int address, int value)
    {
        data[address] = (byte)(value >> 8);
        data[address + 1] = (byte)value;
    }
}
