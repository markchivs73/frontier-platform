using Frontier.TestSupport;
namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.4 tests for <see cref="AgentInvocationProjector"/> (doc 05 §4 step 2).</summary>
public sealed class AgentInvocationProjectorTests
{
    [Fact]
    public void ToAgentInvocation_CopiesFieldsAndDropsCacheChangedFlags()
    {
        var record = TelemetrySamples.Record();

        var invocation = AgentInvocationProjector.ToAgentInvocation(record);

        Assert.Equal(record.CorrelationId, invocation.CorrelationId);
        Assert.Equal(record.NodeId, invocation.NodeId);
        Assert.Equal(record.ArtifactKey, invocation.ArtifactKey);
        Assert.Equal(record.AgentRole, invocation.AgentRole);
        Assert.Equal(record.ResolvedModel, invocation.ResolvedModel);
        Assert.Equal(record.InputContractType, invocation.InputContractType);
        Assert.Equal(record.InputHash, invocation.InputHash);
        Assert.Equal(record.OutputContractType, invocation.OutputContractType);
        Assert.Equal(record.OutputHash, invocation.OutputHash);
        Assert.Equal(record.InputTokens, invocation.InputTokens);
        Assert.Equal(record.OutputTokens, invocation.OutputTokens);
        Assert.Equal(record.CacheReadTokens, invocation.CacheReadTokens);
        Assert.Equal(record.CacheWriteTokens, invocation.CacheWriteTokens);
        Assert.Equal(record.RetryCount, invocation.RetryCount);
        Assert.Equal(record.LatencyMs, invocation.LatencyMs);
        Assert.Equal(record.ToolCalls, invocation.ToolCalls);
        Assert.Equal(record.InvokedAtUtc, invocation.InvokedAtUtc);
    }

    [Fact]
    public void Project_MapsEveryRecord()
    {
        var first = TelemetrySamples.Record();
        var second = first with { CorrelationId = "corr-4", NodeId = "approach-agent" };

        var invocations = AgentInvocationProjector.Project([first, second]);

        Assert.Equal(2, invocations.Count);
        Assert.Equal("corr-3", invocations[0].CorrelationId);
        Assert.Equal("corr-4", invocations[1].CorrelationId);
    }

    [Fact]
    public void Project_NoRecords_ReturnsEmpty()
    {
        var invocations = AgentInvocationProjector.Project([]);

        Assert.Empty(invocations);
    }
}
