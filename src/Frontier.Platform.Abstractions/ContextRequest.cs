using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// Declares the context an agent-task node (<c>AgentTaskNode</c>) needs (doc 04 §3). Context
/// Assembly is the only component that resolves this into a <c>ContextPackage</c>;
/// agents never self-retrieve (doc 00 §2.8). Lives in the platform kernel (ADR-PA2):
/// embedded in subsystem node contracts <em>and</em> consumed by Platform.ContextAssembly.
/// </summary>
public sealed record ContextRequest : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The engagement this request is scoped to.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The role of the agent requesting context, for Model-Role Config resolution.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("agent_role")]
    public required string AgentRole { get; init; }

    /// <summary>Baseline-tier components to include, named from the baseline catalogue. Never <c>"*"</c> (doc 04 §3).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("baseline_components")]
    public required IReadOnlyList<string> BaselineComponents { get; init; }

    /// <summary>Dynamic-tier engagement-context fields to include.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("dynamic_fields")]
    public required IReadOnlyList<string> DynamicFields { get; init; }

    /// <summary>Whether the real-time tier should be assembled for this request. Defaults to <c>false</c> (doc 04 §3).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("requires_real_time")]
    public bool RequiresRealTime { get; init; }

    /// <summary>MCP source ids to fetch for the real-time tier; only meaningful when <see cref="RequiresRealTime"/> is <c>true</c>.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("real_time_sources")]
    public IReadOnlyList<string> RealTimeSources { get; init; } = [];

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (BaselineComponents.Count == 0)
        {
            violations.Add("baseline_components must not be empty.");
        }

        if (BaselineComponents.Contains("*"))
        {
            violations.Add("baseline_components must be component-scoped; '*' is not allowed.");
        }

        if (!RequiresRealTime && RealTimeSources.Count > 0)
        {
            violations.Add("real_time_sources must be empty when requires_real_time is false.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(ContextRequest), violations);
        }
    }
}
