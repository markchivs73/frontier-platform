using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Output of <see cref="AgentTaskActivity"/> — the validated output contract's canonical
/// payload and hash, plus the <see cref="ResolvedModel"/> that produced it, for
/// <see cref="GraphOrchestratorSteps"/> to record a <see cref="StepCompletion"/> (S4.2).
/// </summary>
public sealed record AgentTaskActivityResult
{
    /// <summary>The completed node's <see cref="Abstractions.WorkflowNode.NodeId"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this step produced output for, if any.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The wire type name of the step's output contract.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("output_contract_type")]
    public required string OutputContractType { get; init; }

    /// <summary>The validated output contract's canonical-JSON payload.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("output_payload")]
    public required string OutputPayload { get; init; }

    /// <summary>SHA256 hex hash of <see cref="OutputPayload"/>'s canonical bytes.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("output_hash")]
    public required string OutputHash { get; init; }

    /// <summary>The Model-Role Config resolution that served this step (doc 08 §6).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("resolved_model")]
    public required ResolvedModelSummary ResolvedModel { get; init; }

    /// <summary>
    /// The host build that executed this activity (ADR-E15 pin set, S13.17). Nullable and
    /// additive: results recorded in DTF history before this field existed replay as
    /// <c>null</c> — never <c>required</c>, or replay of in-flight executions would fail
    /// on old recorded results.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("host_build")]
    public string? HostBuild { get; init; }
}
