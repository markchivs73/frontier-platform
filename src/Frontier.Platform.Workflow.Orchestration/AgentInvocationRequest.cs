using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Everything <see cref="IAgentInvoker"/> needs to run one MAF turn (doc 00 §4.3 step 5):
/// the node's instructions (S4.1/doc 14 placeholder), the assembled prompt
/// (<see cref="PromptBuilder"/>), and the model/budget Model-Role Config (S4.3) and
/// Guardrails (S4.5) resolved for this invocation.
/// </summary>
internal sealed record AgentInvocationRequest
{
    /// <summary>The agent's instructions (the content of its <c>instructions/*.md</c> file).</summary>
    internal required string Instructions { get; init; }

    /// <summary>The assembled user-turn prompt: composed context tiers plus the validated input contract payload.</summary>
    internal required string Prompt { get; init; }

    /// <summary>The provider model id resolved by <see cref="Frontier.Platform.ModelRoleConfig.IModelResolver"/> (doc 08 §6).</summary>
    internal required string ModelId { get; init; }

    /// <summary>The output token budget granted by <see cref="Frontier.Platform.Guardrails.IAdmissionController"/> (doc 07).</summary>
    internal required long MaxOutputTokens { get; init; }

    /// <summary>
    /// MCP tools <see cref="IMcpToolCatalog"/> resolved from the node's <c>ToolRefs</c>
    /// (ADR-CD6, S9.25). Empty when the node declares no tools — <see cref="MafAgentInvoker"/>
    /// leaves <see cref="ChatOptions.Tools"/> unset rather than an empty list in that case.
    /// </summary>
    internal IReadOnlyList<AITool> Tools { get; init; } = [];
}
