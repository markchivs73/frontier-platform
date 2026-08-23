using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="AgentTaskActivity"/>'s delegation to <see cref="IAgentTaskActivityPipeline"/>.</summary>
public sealed class AgentTaskActivityTests
{
    private readonly AgentTaskActivity activity = new(new FakeAgentTaskActivityPipeline());

    [Fact]
    public async Task RunAsync_DelegatesToPipeline_ReturnsItsResult()
    {
        var input = BuildInput();

        var result = await activity.RunAsync(new FakeTaskActivityContext(), input);

        var expectedPayload = $"stub-output:{input.NodeId}:{input.CorrelationId}";
        Assert.Equal(input.NodeId, result.NodeId);
        Assert.Equal(input.ArtifactKey, result.ArtifactKey);
        Assert.Equal(input.OutputContractType, result.OutputContractType);
        Assert.Equal(expectedPayload, result.OutputPayload);
        Assert.Equal(CanonicalProfile.Hash(expectedPayload), result.OutputHash);
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => activity.RunAsync(new FakeTaskActivityContext(), null!));
    }

    [Fact]
    public void Constructor_NullPipeline_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivity(null!));
    }

    private static AgentTaskActivityInput BuildInput() => new()
    {
        NodeId = "scope-agent",
        ArtifactKey = "scope",
        Role = "analyst",
        InstructionsRef = "instructions/scope.md",
        InputContractType = "ScopeRequest",
        OutputContractType = "SummaryArtifact",
        CorrelationId = "eng-1::wf-chain::scope-agent::0",
        EngagementId = "eng-1",
        ExecutionId = "eng-1::wf-chain",
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        },
    };
}
