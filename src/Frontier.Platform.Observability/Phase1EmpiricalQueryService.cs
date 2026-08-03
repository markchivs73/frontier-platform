namespace Frontier.Platform.Observability;

/// <summary>
/// Phase 1 stub for <see cref="IEmpiricalQueryService"/> (doc 11 §5): returns empty result
/// sets until the <c>metrics-aggregates</c> change-feed projection (S7+ scope) is built and
/// populated. Callers may display "insufficient data" UI based on zero row counts.
/// </summary>
internal sealed class Phase1EmpiricalQueryService : IEmpiricalQueryService
{
    private static readonly TierEconomics EmptyTierEconomics = new([], 0m, 0);
    private static readonly RetryDistribution EmptyRetryDistribution = new([], 0, 0);
    private static readonly ValidatorDistribution EmptyValidatorDistribution = new([]);
    private static readonly HitlEvidence EmptyHitlEvidence = new([], 0m);

    /// <inheritdoc />
    public Task<TierEconomics> GetCacheEconomicsAsync(EmpiricalScope scope, CancellationToken ct) =>
        Task.FromResult(EmptyTierEconomics);

    /// <inheritdoc />
    public Task<RetryDistribution> GetRetryDistributionAsync(EmpiricalScope scope, CancellationToken ct) =>
        Task.FromResult(EmptyRetryDistribution);

    /// <inheritdoc />
    public Task<ValidatorDistribution> GetValidatorOutcomesAsync(EmpiricalScope scope, CancellationToken ct) =>
        Task.FromResult(EmptyValidatorDistribution);

    /// <inheritdoc />
    public Task<HitlEvidence> GetGateEvidenceAsync(EmpiricalScope scope, CancellationToken ct) =>
        Task.FromResult(EmptyHitlEvidence);

    /// <inheritdoc />
    public Task<NodeMetricsOverlay> GetCanvasOverlayAsync(string workflowId, int definitionVersion, CancellationToken ct) =>
        Task.FromResult(new NodeMetricsOverlay(workflowId, definitionVersion, 0, new Dictionary<string, NodeMetrics>()));
}
