using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class DecisionKindTests
{
    [Fact]
    public void List_Always_ReturnsAllThreeValuesInDeclarationOrder()
    {
        Assert.Equal([DecisionKind.Approve, DecisionKind.Reject, DecisionKind.Escalate], DecisionKind.List);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("escalate")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, DecisionKind.FromName(name).Name);
    }
}
