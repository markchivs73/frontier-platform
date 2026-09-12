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

    /// <summary>
    /// Refreshes only the named dynamic-context components, leaving every other key of the
    /// engagement's context byte-identical (S13.62, ADR-PA24) — the scoped form ADR-CR1's signal
    /// actually describes, since doc 18 §3 raises it for named components and doc 04 §8's payload
    /// carries <c>changed_fields</c> rather than a whole context.
    /// </summary>
    /// <param name="engagementId">The engagement this refresh is for.</param>
    /// <param name="components">Component key → that component's rendered canonical JSON.</param>
    /// <param name="refreshReason">ADR-CR1's explicit reason, for OTEL metrics and structured logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Refresh result: whether the bytes moved, the epoch the engagement is now on, and that epoch's content hash.</returns>
    /// <remarks>
    /// Byte-identity still governs the epoch (ADR-EC1, doc 04 §8): components that merge to the
    /// bytes already stored report <c>Refreshed: false</c> and the unchanged current epoch, so a
    /// primed provider cache is not needlessly invalidated.
    /// </remarks>
    Task<DynamicContextRefreshResult> RefreshComponentsAsync(
        EngagementId engagementId,
        IReadOnlyDictionary<string, string> components,
        string refreshReason,
        CancellationToken ct);
}
