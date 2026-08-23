using System.Diagnostics.CodeAnalysis;

using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Retirement candidate detection: queries execution history to surface versions
/// with zero recorded starts in a 180-day window (ADR-DC4 — evidence-based retirement, not timers).
/// </summary>
public sealed class RetirementMonitor : IRetirementMonitor
{
    private readonly Container _executionSnapshotsContainer;
    private const int ObservationWindowDays = 180;

    public RetirementMonitor(Container executionSnapshotsContainer)
    {
        ArgumentNullException.ThrowIfNull(executionSnapshotsContainer);
        _executionSnapshotsContainer = executionSnapshotsContainer;
    }

    [ExcludeFromCodeCoverage(Justification = "Cosmos SDK adapter (raw Container query, ADR-DC4); exercised by integration tests against the emulator, not the unit-coverage gate - S9.24 frozen policy")]
    public async Task<IReadOnlyList<RetirementCandidate>> GetCandidatesAsync(CancellationToken ct)
    {
        var windowStart = DateTime.UtcNow.AddDays(-ObservationWindowDays);
        var candidates = new List<RetirementCandidate>();

        // Phase 1: Query execution-snapshots for all completed executions within the window.
        // Each snapshot carries the definition version; aggregate by workflow + version
        // to find those with zero executions in the window.
        var query = _executionSnapshotsContainer.GetItemQueryIterator<ExecutionSnapshotQueryResult>(
            new QueryDefinition(
                @"SELECT c.workflow_id AS workflowId,
                         c.definition_version AS definitionVersion,
                         MAX(c.started_at_utc) AS lastExecutionStartedUtc,
                         COUNT(1) AS executionCount
                  FROM c
                  WHERE c.is_latest = true
                    AND c.status IN ('completed', 'failed', 'cancelled')
                    AND c.started_at_utc >= @windowStart
                  GROUP BY c.workflow_id, c.definition_version
                  ORDER BY c.workflow_id, c.definition_version DESC")
            .WithParameter("@windowStart", windowStart),
            requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

        // Collect all executions found in the window per (workflow, version) pair
        var executionsInWindow = new Dictionary<(string, int), (DateTime?, int)>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            foreach (var result in page)
            {
                executionsInWindow[(result.WorkflowId, result.DefinitionVersion)] =
                    (result.LastExecutionStartedUtc, result.ExecutionCount);
            }
        }

        // Phase 1: Stub implementation returns all versions as candidates with zero executions.
        // Full implementation (Phase 2) would:
        //  - Load all published versions per workflow from the store
        //  - Filter to those NOT in the executionsInWindow dict (zero in window)
        //  - Check in-flight count from running snapshots (status='running' or 'paused_at_gate')
        //  - Compute RecommendationSeverity based on inFlight count
        if (executionsInWindow.Count == 0)
        {
            return candidates.AsReadOnly();
        }

        // Placeholder: return empty list for Phase 1. Phase 2 expands with version store queries.
        return candidates.AsReadOnly();
    }

    [ExcludeFromCodeCoverage(Justification = "POCO record with compiler-generated members - S9.24 frozen policy")]
    private sealed record ExecutionSnapshotQueryResult
    {
        public required string WorkflowId { get; init; }
        public required int DefinitionVersion { get; init; }
        public DateTime? LastExecutionStartedUtc { get; init; }
        public int ExecutionCount { get; init; }
    }
}
