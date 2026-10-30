using LeafpileGPT.Desktop;
using Xunit;

namespace LeafpileGPT.Tests;

public sealed class DesktopScreenMetricsTests
{
    [Fact]
    public void ViewportDimensionsBecomeWholeCharacterCells()
    {
        var metrics = DesktopScreenMetrics.FromViewport(803, 601, 10, 20);

        Assert.Equal(new DesktopScreenMetrics(80, 30), metrics);
    }

    [Theory]
    [InlineData(3, 31, 28)]
    [InlineData(4, 30, 28)]
    [InlineData(5, 30, 28)]
    public void PageLimitReservesPromptAndLineTransitionRows(int version, int expectedHeight, int expectedPageLimit)
    {
        var metrics = new DesktopScreenMetrics(80, 30);

        var (height, pageLimit) = metrics.ForStoryVersion(version);

        Assert.Equal(expectedHeight, height);
        Assert.Equal(expectedPageLimit, pageLimit);
    }
}
