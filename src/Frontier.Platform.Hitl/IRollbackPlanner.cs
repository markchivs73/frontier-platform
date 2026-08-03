namespace Frontier.Platform.Hitl;

/// <summary>
/// Computes the section restore/regenerate split for a rejected gate (doc 06 §6).
/// Pure: the orchestrator supplies the cascade's downstream set (already computed via
/// <c>EvaluateCascadeActivity</c>, which excludes the changed section itself) so this
/// planner has no Cascade Logic dependency (library-boundaries).
/// </summary>
public interface IRollbackPlanner
{
    /// <summary>
    /// Plans a rollback to <paramref name="rollbackTargetSection"/> (doc 06 §6):
    /// <see cref="RollbackPlan.InvalidSet"/> is <paramref name="rollbackTargetSection"/>
    /// plus <paramref name="cascadeDownstreamSections"/>;
    /// <see cref="RollbackPlan.RestoreSet"/> is <paramref name="approvedSnapshotRefs"/>
    /// filtered to the sections not in <see cref="RollbackPlan.InvalidSet"/>.
    /// </summary>
    /// <param name="rollbackTargetSection">The section key of the gate's <c>RollbackToNodeId</c> node.</param>
    /// <param name="cascadeDownstreamSections">The downstream set from evaluating the cascade on <paramref name="rollbackTargetSection"/> (excludes itself).</param>
    /// <param name="approvedSnapshotRefs">The execution's current <c>ApprovedSnapshotRefs</c>.</param>
    RollbackPlan Plan(string rollbackTargetSection, IReadOnlyList<string> cascadeDownstreamSections, IReadOnlyDictionary<string, string> approvedSnapshotRefs);
}
