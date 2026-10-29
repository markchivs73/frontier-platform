namespace Frontier.Platform.Contracts.Tests;

public sealed class ContractRefTests
{
    [Theory]
    [InlineData("frontier/evidence-summary/1.0", "frontier/evidence-summary", 1, 0)]
    [InlineData("io.frontier.gen/repo-entry/12.34", "io.frontier.gen/repo-entry", 12, 34)]
    public void TryParse_AWellFormedRef_ReturnsItsParts(string value, string id, int major, int minor)
    {
        Assert.True(ContractRef.TryParse(value, out var parsed));
        Assert.Equal(new ContractRef(id, major, minor), parsed);
        Assert.Equal(value, parsed!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("evidence-summary/1.0")]
    [InlineData("frontier/evidence-summary/1")]
    [InlineData("frontier/evidence_summary/1.0")]
    [InlineData("Frontier/evidence-summary/1.0")]
    [InlineData("frontier/a/b/1.0")]
    [InlineData("frontier/evidence-summary/1234567890.0")]
    public void TryParse_AMalformedRef_ReturnsFalse(string? value)
    {
        Assert.False(ContractRef.TryParse(value, out var parsed));
        Assert.Null(parsed);
    }
}
