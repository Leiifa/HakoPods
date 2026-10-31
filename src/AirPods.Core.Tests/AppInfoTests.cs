using AirPods.Core;
using Xunit;

namespace AirPods.Core.Tests;

/// <summary>
/// Smoke test for the skeleton: proves the test host can load the core assembly.
/// Real coverage starts with the advertisement parser (APW-102).
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void ProductName_is_set()
    {
        Assert.Equal("AirPods for Windows", AppInfo.ProductName);
    }

    [Fact]
    public void Version_is_pre_release()
    {
        Assert.Equal(0, AppInfo.Version.Major);
    }
}
