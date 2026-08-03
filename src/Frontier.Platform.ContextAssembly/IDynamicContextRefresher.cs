using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Persists dynamic context refresh signals via epoch-based versioning (doc 04 §8, ADR-CR1 primitive, C-23).
/// Reads current context, detects byte-level changes, writes new epochs, and emits refresh events.
/// The orchestrator's `WaitForExternalEvent("DynamicContextRefreshed")` and MCP re-fetch logic
/// are deferred to Stage 7-8 (Sense layer); this is the store primitive only.
/// </summary>
public interface IDynamicContextRefresher
{
    /// <summary>
    /// Refreshes engagement-specific dynamic context with new content, returning the refresh outcome.
    /// </summary>
    /// <param name="engagementId">The engagement this refresh is for.</param>
    /// <param name="newDynamicContent">The new dynamic context content (canonical JSON).</param>
    /// <param name="refreshReason">The reason for the refresh (e.g., "periodic", "signal-driven", "manual"),
    /// used for OTEL metrics and structured logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refresh result: Refreshed flag, epoch number, content hash.</returns>
    /// <remarks>
    /// If the new content's canonical hash matches the current content, returns Refreshed: false
    /// without persisting (no needless epoch bump, no cache invalidation). Otherwise writes a new
    /// epoch document, flips the :current pointer, and emits `DynamicContextRefreshed` via
    /// structured logging + OTEL counter (C-22 pattern).
    /// </remarks>
    Task<DynamicContextRefreshResult> RefreshDynamicAsync(
        EngagementId engagementId,
        string newDynamicContent,
        string refreshReason,
        CancellationToken ct);
}
