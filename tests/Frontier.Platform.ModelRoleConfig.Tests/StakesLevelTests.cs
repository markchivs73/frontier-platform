namespace Frontier.Platform.ModelRoleConfig.Tests;

public sealed class StakesLevelTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([StakesLevel.Material, StakesLevel.Standard, StakesLevel.Mechanical], StakesLevel.List);
    }

    [Theory]
    [InlineData("material")]
    [InlineData("standard")]
    [InlineData("mechanical")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, StakesLevel.FromName(name).Name);
    }
}
