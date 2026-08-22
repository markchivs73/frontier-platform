namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class ExecutionModeTests
{
    [Fact]
    public void List_Always_ReturnsBothValuesInDeclarationOrder()
    {
        Assert.Equal([ExecutionMode.OneShot, ExecutionMode.Dispatcher], ExecutionMode.List);
    }

    [Theory]
    [InlineData("one_shot")]
    [InlineData("dispatcher")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ExecutionMode.FromName(name).Name);
    }
}
