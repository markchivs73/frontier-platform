using Frontier.Platform.ModelRoleConfig;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 construction test for <see cref="AgentInvocationRequest"/>.</summary>
public sealed class AgentInvocationRequestTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var target = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];
        var request = new AgentInvocationRequest
        {
            Instructions = "system instructions",
            Prompt = "user prompt",
            ModelId = "claude-fable-5",
            Target = target,
            MaxOutputTokens = 1024,
        };

        Assert.Equal("system instructions", request.Instructions);
        Assert.Equal("user prompt", request.Prompt);
        Assert.Equal("claude-fable-5", request.ModelId);
        Assert.Same(target, request.Target);   // ADR-PA27: the invoker routes on what the role resolved to
        Assert.Equal(1024, request.MaxOutputTokens);
    }
}
