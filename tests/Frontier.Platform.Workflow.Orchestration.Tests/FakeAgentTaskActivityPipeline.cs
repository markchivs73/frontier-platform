using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Deterministic <see cref="IAgentTaskActivityPipeline"/> test double standing in for the
/// real S4.2 pipeline (which calls a live model) across the orchestrator interpreter test
/// suite. Produces a stable <c>"stub-output:{NodeId}:{CorrelationId}"</c> payload, hashed
/// with <see cref="CanonicalProfile"/>, alongside a fixed <see cref="ResolvedModelSummary"/>.
/// </summary>
internal sealed class FakeAgentTaskActivityPipeline : IAgentTaskActivityPipeline
{
    /// <inheritdoc />
    public Task<AgentTaskActivityResult> RunAsync(AgentTaskActivityInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var payload = $"stub-output:{input.NodeId}:{input.CorrelationId}";

        return Task.FromResult(new AgentTaskActivityResult
        {
            NodeId = input.NodeId,
            ArtifactKey = input.ArtifactKey,
            OutputContractType = input.OutputContractType,
            OutputPayload = payload,
            OutputHash = CanonicalProfile.Hash(payload),
            ResolvedModel = new ResolvedModelSummary
            {
                RoleId = input.Role,
                Provider = "anthropic",
                ModelId = "claude-fable-5",
                ChainPosition = 0,
                MappingVersion = 1,
            },
        });
    }
}
