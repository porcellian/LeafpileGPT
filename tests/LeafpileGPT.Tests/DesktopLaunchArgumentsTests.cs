using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class DesktopLaunchArgumentsTests
{
    [Fact]
    public void NoArgumentsMeansStartWithoutAStory()
    {
        Assert.Null(DesktopLaunchArguments.ResolveStoryPath([]));
    }

    [Fact]
    public void ResolvesQuotedPathWithSpaces()
    {
        var path = Path.Combine(Path.GetTempPath(), "Leafpile Test Stories", "zork one.z3");

        var result = DesktopLaunchArguments.ResolveStoryPath([path]);

        Assert.Equal(Path.GetFullPath(path), result);
    }

    [Fact]
    public void JoinsUnquotedPathTokensWithSpaces()
    {
        var result = DesktopLaunchArguments.ResolveStoryPath([Path.Combine("stories", "zork"), "one.z3"]);

        Assert.Equal(Path.GetFullPath(Path.Combine("stories", "zork one.z3")), result);
    }

    [Fact]
    public void RejectsEmptyPathToken()
    {
        Assert.Throws<ArgumentException>(() => DesktopLaunchArguments.ResolveStoryPath(["story.z3", " "]));
    }
}
