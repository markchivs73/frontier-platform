namespace Frontier.Platform.Observability;

/// <summary>
/// Reads the audit store (via the <c>metrics-aggregates</c> change-feed projection for the
/// two hot surfaces — canvas overlay and maturity — and directly against <c>audit-records</c>
/// for ad-hoc empirical queries) per doc 11 §5. Never queries the OTEL backend — empirical
/// conclusions require complete, signed, long-retention evidence (ADR-O1 two-store rule).
/// Phase 1 implementation: <see cref="Phase1EmpiricalQueryService"/> returns empty results
/// until the aggregation layer (S7+) populates the <c>metrics-aggregates</c> container.
/// </summary>
public interface IEmpiricalQueryService
{
    /// <summary>Cache hit rates and cost savings per tier for the given scope (doc 11 §4.1).</summary>
    Task<TierEconomics> GetCacheEconomicsAsync(EmpiricalScope scope, CancellationToken ct);

    /// <summary>Retry reason/model distribution for the given scope (doc 11 §4.2).</summary>
    Task<RetryDistribution> GetRetryDistributionAsync(EmpiricalScope scope, CancellationToken ct);

    /// <summary>Validator outcome distribution for the given scope (doc 11 §4.2).</summary>
    Task<ValidatorDistribution> GetValidatorOutcomesAsync(EmpiricalScope scope, CancellationToken ct);

    /// <summary>HITL gate decision evidence for the given scope (doc 11 §4.2).</summary>
    Task<HitlEvidence> GetGateEvidenceAsync(EmpiricalScope scope, CancellationToken ct);

    /// <summary>Per-node measured-reality overlay for the design canvas (doc 11 §7).</summary>
    Task<NodeMetricsOverlay> GetCanvasOverlayAsync(string workflowId, int definitionVersion, CancellationToken ct);
}
