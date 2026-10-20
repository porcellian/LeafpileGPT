using System.Runtime.InteropServices;

namespace Leafpile.Tests;

public sealed class ProjectSetupTests
{
    [Fact]
    public void TestRunnerUsesNet10()
    {
        Assert.StartsWith(".NET 10.", RuntimeInformation.FrameworkDescription);
    }
}
