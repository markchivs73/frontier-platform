namespace Frontier.Platform.Workflow.Orchestration.Tests;

public sealed class HostBuildInfoTests
{
    [Fact]
    public void Version_IsNonBlankAndStable()
    {
        Assert.False(string.IsNullOrWhiteSpace(HostBuildInfo.Version));
        Assert.Same(HostBuildInfo.Version, HostBuildInfo.Version);   // resolved once per process
    }

    [Fact]
    public void Resolve_NonBlankInformationalVersion_Wins() =>
        Assert.Equal("1.2.3+abc1234", HostBuildInfo.Resolve("1.2.3+abc1234", new Version(9, 9)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankInformationalVersion_FallsBackToAssemblyVersion(string? informational) =>
        Assert.Equal("9.9.0.0", HostBuildInfo.Resolve(informational, new Version(9, 9, 0, 0)));

    [Fact]
    public void Resolve_NothingAvailable_ReturnsUnknown() =>
        Assert.Equal("unknown", HostBuildInfo.Resolve(null, null));
}
