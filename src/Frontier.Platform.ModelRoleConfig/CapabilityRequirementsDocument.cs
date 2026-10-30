using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The stored shape of a role's <see cref="CapabilityRequirements"/> inside a
/// <see cref="RoleDefinitionDocument"/>. The domain record carries no wire names, so the document
/// owns them, as <see cref="ChainEntryDocument"/> does for a chain entry.
/// </summary>
internal sealed record CapabilityRequirementsDocument
{
    /// <summary>The smallest context window (tokens) a chain entry for the role may have.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("min_context_window")]
    public required int MinContextWindow { get; init; }

    /// <summary>Whether the role's agents invoke tools.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("needs_tool_use")]
    public required bool NeedsToolUse { get; init; }

    /// <summary>Whether the role's agents rely on structured output.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("needs_structured_output")]
    public required bool NeedsStructuredOutput { get; init; }

    /// <summary>The latency budget (milliseconds) per invocation.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("max_latency_ms")]
    public required int MaxLatencyMs { get; init; }

    /// <summary>Maps this wire shape onto its domain record.</summary>
    internal CapabilityRequirements ToDomain() => new()
    {
        MinContextWindow = MinContextWindow,
        NeedsToolUse = NeedsToolUse,
        NeedsStructuredOutput = NeedsStructuredOutput,
        MaxLatencyMs = MaxLatencyMs,
    };

    /// <summary>Maps a domain record onto its wire shape.</summary>
    internal static CapabilityRequirementsDocument FromDomain(CapabilityRequirements requirements) => new()
    {
        MinContextWindow = requirements.MinContextWindow,
        NeedsToolUse = requirements.NeedsToolUse,
        NeedsStructuredOutput = requirements.NeedsStructuredOutput,
        MaxLatencyMs = requirements.MaxLatencyMs,
    };
}
