namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class EdgeKindTests
{
    [Fact]
    public void List_Always_ReturnsBothValuesInDeclarationOrder()
    {
        Assert.Equal([EdgeKind.Control, EdgeKind.Data], EdgeKind.List);
    }

    [Theory]
    [InlineData("control")]
    [InlineData("data")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, EdgeKind.FromName(name).Name);
    }
}
