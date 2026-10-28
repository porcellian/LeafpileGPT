using LeafpileGPT.Core;
using Xunit;

namespace LeafpileGPT.Tests;

public class ProjectIdentityTests
{
    [Fact]
    public void ApplicationNameIsLeafpileGpt() => Assert.Equal("LeafpileGPT", ApplicationInfo.Name);
}
