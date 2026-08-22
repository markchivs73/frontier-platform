using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Retry policy reference attached to a <see cref="WorkflowNode"/> (doc 09 §3, doc 10 §5).
/// Policy is data, not code: <see cref="ProfileName"/> resolves to a named Polly profile
/// at execution time; the overrides may only tighten the named profile (lower
/// <see cref="MaxAttemptsOverride"/>, lower <see cref="TimeoutSecondsOverride"/>), never
/// loosen it.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record RetryPolicySpec
{
    /// <summary>The named resilience profile (doc 09 §3, e.g. <c>"llm-default"</c>, <c>"mcp-write"</c>, <c>"none"</c>).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("profile_name")]
    public required string ProfileName { get; init; }

    /// <summary>Optional tightened attempt cap; must not exceed the named profile's own cap.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("max_attempts_override")]
    public int? MaxAttemptsOverride { get; init; }

    /// <summary>Optional tightened per-attempt timeout, in seconds; must not exceed the named profile's own timeout.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("timeout_seconds_override")]
    public int? TimeoutSecondsOverride { get; init; }
}
