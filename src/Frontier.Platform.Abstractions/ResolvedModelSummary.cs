using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// The audit-relevant subset of a Model-Role Config resolution (doc 08 §6), recorded on
/// a <see cref="StepCompletion"/> so the audit trail (Stage 5) can answer "which model
/// produced this section" without Abstractions depending on
/// <c>Frontier.Platform.ModelRoleConfig</c> (library-boundaries: Abstractions is
/// zero-dependency). <c>InvokeAgentActivity</c> (S4.2) projects the platform library's
/// <c>ResolvedModel</c> into this shape.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record ResolvedModelSummary
{
    /// <summary>The role that was resolved, e.g. <c>"deep-reasoning"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("role_id")]
    public required string RoleId { get; init; }

    /// <summary>The resolved model's provider, e.g. <c>"anthropic"</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>The resolved model's identifier, e.g. <c>"claude-fable-5"</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("model_id")]
    public required string ModelId { get; init; }

    /// <summary>The resolved model's version, if the provider reports one.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("model_version")]
    public string? ModelVersion { get; init; }

    /// <summary>Position in the mapping's chain that was served: 0 = primary, &gt;0 = fallback (doc 08 §4 ADR-M2).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("chain_position")]
    public required int ChainPosition { get; init; }

    /// <summary>The pinned mapping version this resolution was made under.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("mapping_version")]
    public required int MappingVersion { get; init; }
}
