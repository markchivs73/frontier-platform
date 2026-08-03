namespace Frontier.Platform.Serialization.Tests;

public sealed class StartupCheckResultTests
{
    [Fact]
    public void Pass_ReturnsPassedTrueWithNoFailureReason()
    {
        var result = StartupCheckResult.Pass();

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Fail_ReturnsPassedFalseWithGivenReason()
    {
        var result = StartupCheckResult.Fail("drift detected");

        Assert.False(result.Passed);
        Assert.Equal("drift detected", result.FailureReason);
    }
}
