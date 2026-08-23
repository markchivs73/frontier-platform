using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Pure mapping from an <see cref="ExecutionSnapshot"/> to a sandbox test-run's outcome
/// (doc 13 §5, S9.38a) — extracted from <see cref="TestRunService"/> so the mapping is
/// testable without a real orchestration.
/// </summary>
internal static class TestRunExecution
{
    /// <summary>
    /// A run has finished — no further polling can change its result.
    /// <see cref="ExecutionStatus.PausedOnFailure"/> (S9.45) is terminal here even though
    /// it reads as "paused": nothing ever writes a further snapshot after it (the
    /// orchestration instance faults right behind the checkpoint, doc 03 §9, no resume
    /// path exists), so waiting past it would just time out instead of ever completing.
    /// </summary>
    internal static bool IsTerminal(ExecutionStatus status) =>
        status == ExecutionStatus.Completed || status == ExecutionStatus.Failed
        || status == ExecutionStatus.Cancelled || status == ExecutionStatus.PausedOnFailure;

    /// <summary>
    /// S9.53 (doc 19 A4-R4): a structured per-step row for every node that has completed so far
    /// (nodes not yet reached are simply absent), carrying the metadata the snapshot holds —
    /// node type, output contract/hash, the section it wrote, retry count, completion time, and a
    /// derived per-step duration (delta from the previous completed step). <c>OutputContent</c> is
    /// left null here; <see cref="TestRunService.GetResultAsync"/> fills it from the section store.
    /// </summary>
    internal static IReadOnlyList<TestRunNodeStep> BuildNodeSteps(ExecutionSnapshot snapshot)
    {
        var ordered = snapshot.CompletedSteps.OrderBy(s => s.CompletedAtUtc).ToList();
        var steps = new List<TestRunNodeStep>(ordered.Count);
        DateTime? previous = null;
        foreach (var s in ordered)
        {
            steps.Add(new TestRunNodeStep
            {
                NodeId = s.NodeId,
                Status = "completed",
                NodeType = s.NodeType.Name,
                OutputContractType = s.OutputContractType,
                OutputHash = s.OutputHash,
                ArtifactKey = s.ArtifactKey,
                RetryCount = s.RetryCount,
                CompletedAtUtc = s.CompletedAtUtc,
                DurationMs = previous is { } prev ? (int)(s.CompletedAtUtc - prev).TotalMilliseconds : null,
                // S13.31: a decision produces no payload — the branch it chose is its result.
                SelectedBranchNodeId = s.SelectedBranchNodeId,
            });
            previous = s.CompletedAtUtc;
        }

        steps.AddRange(BuildSkippedSteps(snapshot));
        return steps;
    }

    /// <summary>
    /// S13.31 (doc 19 A4-R4): nodes the walk skipped because every path to them was dead —
    /// unselected <c>decision</c> branch subtrees (ADR-5 D6). Without these rows a skipped
    /// node simply vanishes from the feed, which reads like a bug rather than routing.
    /// Appended in skip order after the completed steps: a skipped node has no completion
    /// time, so it cannot be interleaved honestly.
    /// </summary>
    internal static IEnumerable<TestRunNodeStep> BuildSkippedSteps(ExecutionSnapshot snapshot) =>
        (snapshot.SkippedNodeIds ?? []).Select(nodeId => new TestRunNodeStep
        {
            NodeId = nodeId,
            Status = "skipped",
        });

    /// <summary>Maps a terminal or still-paused snapshot to the outcome persisted on the test-run document.</summary>
    internal static TestRunOutcome ToOutcome(ExecutionSnapshot snapshot) => new(
        Success: snapshot.Status == ExecutionStatus.Completed,
        NodeSteps: BuildNodeSteps(snapshot),
        ErrorMessage: snapshot.Status switch
        {
            var s when s == ExecutionStatus.Failed => "Test-run execution failed.",
            var s when s == ExecutionStatus.PausedAtGate => $"Test-run paused at gate '{snapshot.PausedAtGateId}' awaiting an interactive decision (gateMode=interactive; not auto-handled).",
            var s when s == ExecutionStatus.PausedOnFailure => $"Test-run failed permanently at node '{snapshot.CurrentNodeId}' ({snapshot.FailureClassification}).",
            var s when s == ExecutionStatus.Cancelled => "Test-run was cancelled.",
            _ => null
        },
        PausedAtGateId: snapshot.Status == ExecutionStatus.PausedAtGate ? snapshot.PausedAtGateId : null,
        GateKind: null,
        GateDecisions: BuildGateDecisions(snapshot),
        // C-35 (S9.53): the anchor for a plain-failure canvas link — the last *completed* node
        // (not necessarily the one that failed; exact attribution is deferred, see S9.45).
        FailureNodeId: (snapshot.Status == ExecutionStatus.PausedOnFailure || snapshot.Status == ExecutionStatus.Failed)
            ? snapshot.CurrentNodeId
            : null);

    /// <summary>
    /// S9.29g (doc 13 §5 "gate decisions taken"): every <see cref="HitlDecision"/> DTF has
    /// recorded on the snapshot so far, whether auto-approved (<see cref="TestRunService"/>'s
    /// own auto-approve path) or designer-decided (interactive mode) — real evidence, not a
    /// hardcoded empty list.
    /// </summary>
    private static IReadOnlyList<TestRunGateDecision> BuildGateDecisions(ExecutionSnapshot snapshot) =>
        [.. snapshot.Decisions.Select(ToGateDecision)];

    /// <summary><see cref="TestRunGateOutcome"/> is binary (approved/rejected); <see cref="DecisionKind.Escalate"/> collapses to rejected — sandbox test-runs have no escalation-routing concept (doc 13 §5).</summary>
    private static TestRunGateDecision ToGateDecision(HitlDecision decision) => new()
    {
        GateId = decision.GateId,
        Outcome = decision.Kind == DecisionKind.Approve ? TestRunGateOutcome.Approved : TestRunGateOutcome.Rejected,
        Note = decision.Notes,
        DecidedAtUtc = decision.DecidedAtUtc,
    };

    /// <summary>
    /// S9.38d: the <see cref="HumanGateNode.GateKind"/> for <paramref name="gateId"/> in
    /// <paramref name="definition"/> — resolved by the caller (not <see cref="ToOutcome"/>,
    /// which only has the snapshot) once <see cref="TestRunOutcome.PausedAtGateId"/> is known,
    /// so the A4 UI's gate-pending card can show it.
    /// </summary>
    internal static string? ResolveGateKind(WorkflowDefinition definition, string? gateId) =>
        gateId is null
            ? null
            : definition.Nodes.OfType<HumanGateNode>().FirstOrDefault(n => n.NodeId == gateId)?.GateKind.Name;
}

/// <summary>The outcome of waiting for a sandbox test-run to reach a terminal or gate-paused state.</summary>
internal sealed record TestRunOutcome(
    bool Success,
    IReadOnlyList<TestRunNodeStep> NodeSteps,
    string? ErrorMessage,
    string? PausedAtGateId,
    string? GateKind,
    IReadOnlyList<TestRunGateDecision> GateDecisions,
    string? FailureNodeId);
