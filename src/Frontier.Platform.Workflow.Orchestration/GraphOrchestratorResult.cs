using System.Text.Json.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Output of <see cref="GraphOrchestrator"/>: the steps executed and the resulting
/// section statuses (doc 00 §3.4). <see cref="SnapshotStateActivity"/> (S2.4) projects
/// this into Cosmos at each checkpoint; this record is the orchestration's final result.
/// </summary>
public sealed record GraphOrchestratorResult
{
    /// <summary>Completed steps, in execution order.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("completed_steps")]
    public required IReadOnlyList<StepCompletion> CompletedSteps { get; init; }

    /// <summary>Final status of every section this workflow produces, keyed by section key.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("section_statuses")]
    public required IReadOnlyDictionary<string, ArtifactStatus> ArtifactStatuses { get; init; }
}
