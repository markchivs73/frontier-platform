using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class GateKindTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([GateKind.Intake, GateKind.Business, GateKind.Technical], GateKind.List);
    }

    [Theory]
    [InlineData("intake")]
    [InlineData("business")]
    [InlineData("technical")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, GateKind.FromName(name).Name);
    }
}
