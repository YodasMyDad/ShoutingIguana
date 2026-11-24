using System.Runtime.InteropServices;
using ShoutingIguana.Engine.Fetching;

namespace ShoutingIguana.Engine.Tests.Fetching;

public sealed class BrowserHostPlatformTests
{
    /// <summary>As Ubuntu 26.04 ships it: Playwright 1.56 asked for "ubuntu26.04-x64", which has no Chromium.</summary>
    private const string UBUNTU_26_04 = """
        PRETTY_NAME="Ubuntu 26.04.1 LTS"
        NAME="Ubuntu"
        VERSION_ID="26.04"
        VERSION="26.04.1 LTS (Resolute Raccoon)"
        ID=ubuntu
        ID_LIKE=debian
        """;

    [Fact]
    public void AnUbuntuPlaywrightHasNoBuildForUsesTheNewestBuildItHas()
    {
        Assert.Equal("ubuntu24.04-x64", BrowserHostPlatform.OverrideFor(UBUNTU_26_04, Architecture.X64));
        Assert.Equal("ubuntu24.04-arm64", BrowserHostPlatform.OverrideFor(UBUNTU_26_04, Architecture.Arm64));
    }

    [Theory]
    [InlineData("ID=pop\nVERSION_ID=\"26.04\"")]
    [InlineData("ID=neon\nVERSION_ID=\"26.04\"")]
    [InlineData("ID=tuxedo\nVERSION_ID=\"26.04\"")]
    [InlineData("VERSION_ID=\"27.10\"\nID=ubuntu")]
    [InlineData("ID=\"ubuntu\"\r\nVERSION_ID=\"28.04\"\r\n")]
    public void DistributionsThatKeepUbuntusVersionsDoToo(string osRelease) =>
        Assert.Equal("ubuntu24.04-x64", BrowserHostPlatform.OverrideFor(osRelease, Architecture.X64));

    [Theory]
    [InlineData("ID=ubuntu\nVERSION_ID=\"24.04\"")]
    [InlineData("ID=ubuntu\nVERSION_ID=\"25.10\"")]
    [InlineData("ID=ubuntu\nVERSION_ID=\"22.04\"")]
    [InlineData("ID=debian\nVERSION_ID=\"13\"")]
    [InlineData("ID=debian\nVERSION_ID=\"14\"")]
    [InlineData("ID=linuxmint\nVERSION_ID=\"26\"")]
    [InlineData("ID=fedora\nVERSION_ID=43")]
    [InlineData("ID=ubuntu")]
    [InlineData("ID=ubuntu\nVERSION_ID=rolling")]
    [InlineData("")]
    public void EverySystemPlaywrightKnowsOrFallsBackOnIsLeftToIt(string osRelease) =>
        Assert.Null(BrowserHostPlatform.OverrideFor(osRelease, Architecture.X64));

    [Fact]
    public void AnArchitecturePlaywrightHasNoBuildForIsLeftToIt() =>
        Assert.Null(BrowserHostPlatform.OverrideFor(UBUNTU_26_04, Architecture.Arm));
}
