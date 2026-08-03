using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class ExecutionStatusTests
{
    [Fact]
    public void List_Always_ReturnsAllSixValuesInDeclarationOrder()
    {
        Assert.Equal(
            [
                ExecutionStatus.Running,
                ExecutionStatus.PausedAtGate,
                ExecutionStatus.PausedOnFailure,
                ExecutionStatus.Completed,
                ExecutionStatus.Failed,
                ExecutionStatus.Cancelled,
            ],
            ExecutionStatus.List);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("paused_at_gate")]
    [InlineData("paused_on_failure")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ExecutionStatus.FromName(name).Name);
    }
}
