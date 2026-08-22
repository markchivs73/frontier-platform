using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// A compiled, versioned, hashed workflow graph (doc 00 §3.1) — the design-time
/// artifact a chat-designer session publishes and the <c>GraphOrchestrator</c>
/// interprets at execution time (ADR-1, ADR-2). Immutable once published: an edit
/// produces <see cref="DefinitionVersion"/> + 1; running executions stay pinned to the
/// version they started with.
/// </summary>
public sealed record WorkflowDefinition : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "2.0"; // S13.12a: the ADR-E3a D3 artifact rename is a wire break (ArtifactVocabularyMigration adapts 1.0 bytes).

    /// <summary>Stable identity for this workflow, independent of <see cref="DefinitionVersion"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("workflow_id")]
    public required string WorkflowId { get; init; }

    /// <summary>Bumped on every published edit (doc 00 §3.1).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("definition_version")]
    public required int DefinitionVersion { get; init; }

    /// <summary>The engagement type this workflow runs against, e.g. <c>"support-triage"</c>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("engagement_type")]
    public required string EngagementType { get; init; }

    /// <summary>Human-readable workflow name.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The workflow's nodes (doc 00 §3.2).</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("nodes")]
    public required IReadOnlyList<WorkflowNode> Nodes { get; init; }

    /// <summary>The workflow's edges (doc 00 §3.3).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("edges")]
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }

    /// <summary>SHA256 hex hash of this definition's canonical bytes, excluding this field itself (doc 01).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("definition_hash")]
    public required string DefinitionHash { get; init; }

    /// <summary>Run-to-completion (<see cref="ExecutionMode.OneShot"/>) or eternal router (<see cref="ExecutionMode.Dispatcher"/>) (doc 00 §4.4).</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("mode")]
    public required ExecutionMode Mode { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        violations.AddRange(WorkflowDefinitionValidator.ValidateUniqueNodeIds(Nodes));
        violations.AddRange(WorkflowDefinitionValidator.ValidateEdgesResolve(Nodes, Edges));
        violations.AddRange(WorkflowDefinitionValidator.ValidateAcyclic(Nodes, Edges));

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(WorkflowDefinition), violations);
        }
    }
}
