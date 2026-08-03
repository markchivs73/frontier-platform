namespace Frontier.Platform.Observability;

/// <summary>
/// Phase 1 stub for <see cref="IMaturityTracker"/> (doc 11 §6): no assessments until
/// the <c>metrics-aggregates</c> aggregation layer (S7+) populates the statistics the
/// evaluator needs. Callers display "no assessments yet" rather than fabricated bands.
/// </summary>
internal sealed class Phase1MaturityTracker : IMaturityTracker
{
    /// <inheritdoc />
    public Task<MaturityAssessment?> GetAsync(string agentRole, string engagementType, CancellationToken ct) =>
        Task.FromResult<MaturityAssessment?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<MaturityAssessment>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MaturityAssessment>>([]);
}
