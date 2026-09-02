using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Mutable in-memory accumulator for <see cref="GraphOrchestrator"/>'s walk (S2.2). Folded
/// into <see cref="GraphOrchestratorResult"/> at the end of the orchestration; not itself
/// a wire contract.
/// </summary>
internal sealed class GraphExecutionState
{
    /// <summary>
    /// When the walk started, from <c>context.CurrentUtcDateTime</c> — deterministic under replay
    /// and stamped onto every checkpoint so a runs list can order by start rather than by last
    /// activity (ADR-EX1).
    /// </summary>
    internal required DateTime StartedAtUtc { get; init; }

    /// <summary>Completed steps, in execution order.</summary>
    internal List<StepCompletion> CompletedSteps { get; } = [];

    /// <summary>Current status of every section produced so far, keyed by section key.</summary>
    internal Dictionary<string, ArtifactStatus> ArtifactStatuses { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The latest <c>artifact-state</c> version number written for each section, keyed by
    /// section key (doc 02 §2-3): incremented by <see cref="GraphOrchestratorSteps.WriteArtifactVersionAsync"/>
    /// before each write, starting from 1, never derived from wall-clock time.
    /// </summary>
    internal Dictionary<string, int> ArtifactVersions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The next <see cref="ExecutionSnapshot.Sequence"/> to write (doc 02 §5): a
    /// monotonic checkpoint counter incremented by <see cref="GraphOrchestratorSteps.WriteSnapshotAsync"/>,
    /// never derived from wall-clock time. Starts at <c>1</c> because sequence <c>0</c>
    /// is reserved for the Host's <c>OrchestrationFactory</c> pre-start projection
    /// (S4.7a, doc 02 §5) — the orchestrator's first checkpoint must not overwrite it.
    /// </summary>
    internal int Sequence { get; set; } = 1;

    /// <summary>
    /// Each completed node's canonical-JSON output payload, keyed by <see cref="Abstractions.WorkflowNode.NodeId"/>
    /// (S4.2). Populated from each <see cref="AgentTaskActivityResult.OutputPayload"/> as
    /// it returns from <see cref="GraphOrchestratorSteps.RunNodeAsync"/> — sourced purely
    /// from historized activity results, so re-deriving it on replay is deterministic
    /// (DTF determinism skill). <see cref="GraphOrchestratorSteps.BuildActivityInput"/> uses
    /// this to populate a downstream node's <see cref="AgentTaskActivityInput.UpstreamPayload"/>
    /// from its Data-edge predecessor.
    /// </summary>
    internal Dictionary<string, string> NodeOutputPayloads { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The latest <c>artifact-state</c> document ref written for each section, keyed by
    /// section key (S4.6, doc 06 §9): populated by <see cref="GraphOrchestratorSteps.WriteArtifactVersionAsync"/>
    /// and <see cref="GraphOrchestratorSteps.RestoreArtifactsAsync"/>. Surfaced to a
    /// <see cref="HumanGateNode"/>'s approval request as <see cref="Frontier.Platform.Hitl.GateOpenRequest.SectionRefs"/>.
    /// </summary>
    internal Dictionary<string, string> SectionRefs { get; } = new(StringComparer.Ordinal);

    /// <summary>Human gate decisions recorded so far, in decision order (doc 02 §2, doc 06 §3). Folded into <see cref="ExecutionSnapshot.Decisions"/>.</summary>
    internal List<HitlDecision> Decisions { get; } = [];

    /// <summary>Artifact key → <c>artifact-state</c> ref of its last-approved version (doc 06 §6), for rollback. Folded into <see cref="ExecutionSnapshot.ApprovedSnapshotRefs"/>.</summary>
    internal Dictionary<string, string> ApprovedSnapshotRefs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many times each <see cref="HumanGateNode"/> has been opened, keyed by
    /// <see cref="Abstractions.WorkflowNode.NodeId"/> (doc 06 §4, §13): <c>0</c> on first
    /// entry, incremented on every re-entry after a rejection cascade, used as
    /// <see cref="Frontier.Platform.Hitl.GateOpenRequest.Occurrence"/>.
    /// </summary>
    internal Dictionary<string, int> GateOccurrences { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many times each node has been run, keyed by <see cref="Abstractions.WorkflowNode.NodeId"/>
    /// (ADR-5 Decision 5, S13.7i): mints the correlation id's third segment
    /// (<c>{executionId}::{nodeId}::{occurrence}</c>) — deterministic and collision-free
    /// under concurrent branches, unlike the retired shared
    /// <see cref="CompletedSteps"/>-count form, while still disambiguating cascade
    /// re-runs of the same node.
    /// </summary>
    internal Dictionary<string, int> NodeOccurrences { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Nodes skipped because every path to them was dead — unselected
    /// <see cref="Abstractions.DecisionNode"/> branch subtrees (ADR-5 Decision 6,
    /// S13.7j), in skip order. Folded into <see cref="ExecutionSnapshot.SkippedNodeIds"/>
    /// (omitted while empty, so pre-S13.7j snapshots are byte-identical).
    /// </summary>
    internal List<string> SkippedNodeIds { get; } = [];
}
