using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Evaluates downstream impact after a section update (doc 00 §3.2). Compiles to
/// <c>EvaluateCascadeActivity</c>, which derives the dependency graph from typed data
/// edges and computes the downstream set to re-walk (doc 00 §3.3, ADR-3).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record CascadeCheckNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.CascadeCheck;

    /// <summary>Artifact keys whose update should trigger this cascade evaluation.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("trigger_artifact_keys")]
    public required IReadOnlyList<string> TriggerArtifactKeys { get; init; }
}
