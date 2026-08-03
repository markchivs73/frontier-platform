namespace Frontier.Platform.Observability;

/// <summary>
/// Retrieves maturity assessments per (agent_role × engagement_type) over the rolling
/// evaluation window (default 90 days, minimum 20 invocations per doc 11 §6). Phase 1
/// implementation: <see cref="Phase1MaturityTracker"/> returns no assessments until the
/// <c>metrics-aggregates</c> aggregation layer (S7+) supplies the required statistics.
/// </summary>
public interface IMaturityTracker
{
    /// <summary>Returns the current assessment for the given pair, or <c>null</c> if no data.</summary>
    Task<MaturityAssessment?> GetAsync(string agentRole, string engagementType, CancellationToken ct);

    /// <summary>Returns all current assessments (the maturity board, doc 11 §6).</summary>
    Task<IReadOnlyList<MaturityAssessment>> GetAllAsync(CancellationToken ct);
}
