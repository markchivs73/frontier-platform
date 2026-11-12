using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Abstractions.Tests;

public sealed class ExecutionStatusTests
{
    [Fact]
    public void List_Always_ReturnsAllSevenValuesInDeclarationOrder()
    {
        Assert.Equal(
            [
                ExecutionStatus.Running,
                ExecutionStatus.PausedAtGate,
#pragma warning disable CS0618 // The legacy value stays listed so old snapshots deserialize (ADR-PA47).
                ExecutionStatus.PausedOnFailure,
#pragma warning restore CS0618
                ExecutionStatus.Completed,
                ExecutionStatus.Failed,
                ExecutionStatus.Cancelled,
                ExecutionStatus.Terminated,
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
    [InlineData("terminated")]
    public void FromName_KnownName_RoundTrips(string name)
    {
        Assert.Equal(name, ExecutionStatus.FromName(name).Name);
    }

    /// <summary>S13.149: the new value's wire token is pinned, not inferred from the member name.</summary>
    [Fact]
    public void Terminated_WireName_IsTerminated() =>
        Assert.Equal("terminated", ExecutionStatus.Terminated.Name);
}
