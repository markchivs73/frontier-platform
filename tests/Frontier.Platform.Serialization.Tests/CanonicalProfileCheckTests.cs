namespace Frontier.Platform.Serialization.Tests;

public sealed class CanonicalProfileCheckTests
{
    [Fact]
    public void Name_ReturnsCanonicalProfile()
    {
        var check = new CanonicalProfileCheck();

        Assert.Equal("CanonicalProfile", check.Name);
    }

    [Fact]
    public async Task CheckAsync_FixtureMatchesCommittedHash_ReturnsPass()
    {
        var check = new CanonicalProfileCheck();

        var result = await check.CheckAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Evaluate_HashMismatch_ReturnsFailWithDriftReason()
    {
        var result = CanonicalProfileCheck.Evaluate(new string('0', 64));

        Assert.False(result.Passed);
        Assert.Contains("Canonical serialization drift", result.FailureReason, StringComparison.Ordinal);
    }
}
