namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// One entry in a <see cref="RoleMapping"/>'s chain (doc 08 §4, ADR-PA27): a model the platform
/// calls through a chat client (<see cref="ModelEntry"/>), or a registered remote agent reached over
/// A2A (<see cref="AgentEntry"/>). A chain holds one kind only — see <see cref="ChainShape"/>.
/// </summary>
public abstract record ChainEntry
{
    /// <summary>The provider, e.g. <c>"anthropic"</c>, <c>"azure-openai"</c>, or <see cref="AgentEntry.A2aProvider"/>.</summary>
    public required string Provider { get; init; }

    /// <summary>ISO 4217 code of this entry's costs, e.g. <c>"USD"</c> (ADR-PA21).</summary>
    public required string Currency { get; init; }

    /// <summary>What this entry targets, as the circuit breaker and audit key it: the model id, or the agent's resource name.</summary>
    public abstract string TargetId { get; }
}
