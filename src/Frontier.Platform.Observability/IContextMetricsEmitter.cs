namespace Frontier.Platform.Observability;

/// <summary>
/// Emits observability metrics for context assembly outcomes (S3.4): per-tier cache hits,
/// token usage, and cost deltas. Called post-invocation by the orchestration or agent
/// pipeline to record what happened with caching.
/// </summary>
public interface IContextMetricsEmitter
{
    /// <summary>
    /// Record tier metrics for an execution instance.
    /// </summary>
    /// <param name="executionId">Execution/instance ID for correlation.</param>
    /// <param name="metrics">Per-tier metrics (Baseline, Dynamic, Real-Time).</param>
    /// <param name="ct">Cancellation token.</param>
    Task EmitTierMetricsAsync(string executionId, IReadOnlyList<ContextTierMetrics> metrics, CancellationToken ct = default);

    /// <summary>
    /// Retrieve aggregated metrics for an engagement (for dashboards, cost reporting, etc.).
    /// Returns null if no metrics recorded for the engagement yet.
    /// </summary>
    /// <param name="engagementId">Engagement ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EngagementMetricsSnapshot?> GetEngagementMetricsAsync(string engagementId, CancellationToken ct = default);
}
