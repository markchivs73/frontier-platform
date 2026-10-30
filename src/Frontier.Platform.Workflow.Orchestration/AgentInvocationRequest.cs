using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Everything <see cref="IAgentInvoker"/> needs to run one MAF turn (doc 00 §4.3 step 5):
/// the node's instructions (S4.1/doc 14 placeholder), the assembled prompt
/// (<see cref="PromptBuilder"/>), and the model/budget Model-Role Config (S4.3) and
/// Guardrails (S4.5) resolved for this invocation.
/// </summary>
public sealed record AgentInvocationRequest
{
    /// <summary>The agent's instructions (the content of its <c>instructions/*.md</c> file); empty for an agent target whose node names no ref (ADR-PA40).</summary>
    public required string Instructions { get; init; }

    /// <summary>The assembled user-turn prompt: composed context tiers plus the validated input contract payload.</summary>
    public required string Prompt { get; init; }

    /// <summary>The provider model id resolved by <see cref="Frontier.Platform.ModelRoleConfig.IModelResolver"/> (doc 08 §6); an agent target's resource name.</summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The resolved chain entry (ADR-PA27), so the invoker can route on what the role resolved to: a
    /// <see cref="Frontier.Platform.ModelRoleConfig.ModelEntry"/> carries its provider, endpoint and
    /// deployment; an <see cref="Frontier.Platform.ModelRoleConfig.AgentEntry"/> names the registry
    /// resource to call over A2A.
    /// </summary>
    public required Frontier.Platform.ModelRoleConfig.ChainEntry Target { get; init; }

    /// <summary>The output token budget granted by <see cref="Frontier.Platform.Guardrails.IAdmissionController"/> (doc 07).</summary>
    public required long MaxOutputTokens { get; init; }

    /// <summary>
    /// MCP tools <see cref="IMcpToolCatalog"/> resolved from the node's <c>ToolRefs</c>
    /// (ADR-CD6, S9.25). Empty when the node declares no tools — <see cref="MafAgentInvoker"/>
    /// leaves <see cref="ChatOptions.Tools"/> unset rather than an empty list in that case.
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; init; } = [];

    /// <summary>
    /// The structured-output format the engine asks the model to follow, built from the node's
    /// output contract (ADR-E27 D7). A generation aid only: the engine validates the reply whatever
    /// the provider did. <see langword="null"/> when there is none to ask for; an invoker whose
    /// protocol has no response format (A2A) ignores it.
    /// </summary>
    public ChatResponseFormat? ResponseFormat { get; init; }
}
