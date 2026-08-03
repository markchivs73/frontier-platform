using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Manages dynamic context for an engagement (engagement-specific, moderate refresh cadence).
/// Supports refresh on signal (ADR-CR1) and versioned storage.
/// </summary>
public interface IEngagementContextStore
{
    /// <summary>
    /// Retrieve the current dynamic context for an engagement.
    /// </summary>
    /// <param name="engagementId">The engagement ID (partition key).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current dynamic context (JSON), or null if not yet initialized.</returns>
    Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct);

    /// <summary>
    /// Upsert dynamic context for an engagement (e.g., after a refresh signal).
    /// </summary>
    /// <param name="engagementId">The engagement ID (partition key).</param>
    /// <param name="dynamicContent">New dynamic context (JSON).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The epoch number after the upsert (0-based).</returns>
    Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct);
}
