using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// What a <see cref="RoleDefinition"/> needs from any model in its mapping's chain (doc
/// 08 §4): the requirements profile a candidate model must satisfy to be eligible for
/// the role, independent of which provider/model is currently mapped.
/// </summary>
public sealed record CapabilityRequirements
{
    /// <summary>The smallest context window (tokens) a chain entry for this role may have.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("min_context_window")]
    public required int MinContextWindow { get; init; }

    /// <summary>Whether this role's agents invoke tools/function-calling.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("needs_tool_use")]
    public required bool NeedsToolUse { get; init; }

    /// <summary>Whether this role's agents rely on reliable structured (typed-contract) output.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("needs_structured_output")]
    public required bool NeedsStructuredOutput { get; init; }

    /// <summary>The latency budget (milliseconds) this role's callers expect per invocation.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("max_latency_ms")]
    public required int MaxLatencyMs { get; init; }
}
