namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// What a <see cref="RoleDefinition"/> needs from any model in its mapping's chain (doc
/// 08 §4): the requirements profile a candidate model must satisfy to be eligible for
/// the role, independent of which provider/model is currently mapped.
/// </summary>
public sealed record CapabilityRequirements
{
    /// <summary>The smallest context window (tokens) a chain entry for this role may have.</summary>
    public required int MinContextWindow { get; init; }

    /// <summary>Whether this role's agents invoke tools/function-calling.</summary>
    public required bool NeedsToolUse { get; init; }

    /// <summary>Whether this role's agents rely on reliable structured (typed-contract) output.</summary>
    public required bool NeedsStructuredOutput { get; init; }

    /// <summary>The latency budget (milliseconds) this role's callers expect per invocation.</summary>
    public required int MaxLatencyMs { get; init; }
}
