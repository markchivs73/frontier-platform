using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Hitl;

/// <summary>
/// The result of <see cref="IRollbackPlanner.Plan"/> (doc 06 §6): which sections must
/// be restored from their approved snapshots and which must be regenerated.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by RollbackPlanner tests.")]
public sealed record RollbackPlan
{
    /// <summary>
    /// The rollback target's section plus everything downstream of it (doc 06 §6):
    /// these sections become <c>regenerating</c> and are re-run by
    /// <c>GraphOrchestratorSteps.RegenerateDownstreamAsync</c>.
    /// </summary>
    public required IReadOnlyList<string> InvalidSet { get; init; }

    /// <summary>
    /// Section key → approved snapshot ref (doc 06 §6) for every section that keeps its
    /// current approval — i.e. <c>ExecutionSnapshot.ApprovedSnapshotRefs</c> minus
    /// <see cref="InvalidSet"/>. Passed to <c>RestoreSectionsActivity</c> to repoint those
    /// sections' <c>current</c> documents.
    /// </summary>
    public required IReadOnlyDictionary<string, string> RestoreSet { get; init; }
}
