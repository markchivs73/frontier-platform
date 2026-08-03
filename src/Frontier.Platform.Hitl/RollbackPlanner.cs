namespace Frontier.Platform.Hitl;

/// <inheritdoc cref="IRollbackPlanner" />
public sealed class RollbackPlanner : IRollbackPlanner
{
    /// <inheritdoc />
    public RollbackPlan Plan(string rollbackTargetSection, IReadOnlyList<string> cascadeDownstreamSections, IReadOnlyDictionary<string, string> approvedSnapshotRefs)
    {
        ArgumentNullException.ThrowIfNull(rollbackTargetSection);
        ArgumentNullException.ThrowIfNull(cascadeDownstreamSections);
        ArgumentNullException.ThrowIfNull(approvedSnapshotRefs);

        var invalidSet = new List<string> { rollbackTargetSection };
        invalidSet.AddRange(cascadeDownstreamSections);

        var restoreSet = approvedSnapshotRefs
            .Where(pair => !invalidSet.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new RollbackPlan { InvalidSet = invalidSet, RestoreSet = restoreSet };
    }
}
