namespace Frontier.Platform.ModelRoleConfig.Tests;

public sealed class RolloutRingTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([RolloutRing.Shadow, RolloutRing.Canary, RolloutRing.Fleet], RolloutRing.List);
    }

    [Theory]
    [InlineData("shadow")]
    [InlineData("canary")]
    [InlineData("fleet")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, RolloutRing.FromName(name).Name);
    }
}
