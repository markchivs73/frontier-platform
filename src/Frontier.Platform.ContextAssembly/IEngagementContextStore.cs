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

    /// <summary>
    /// Write only the named <paramref name="components"/> into an engagement's dynamic context,
    /// leaving every other key byte-identical (S13.62, ADR-PA24, ADR-CR1).
    /// <para>
    /// This is the primitive a <em>scoped</em> refresh uses.
    /// <see cref="UpsertDynamicContextAsync"/> replaces the whole document, which on a live
    /// engagement deletes keys the refresh never produced — and a later request for one of them
    /// throws <see cref="Frontier.Platform.Abstractions.ContractViolationException"/>, a permanent
    /// failure per hard invariant 7 with no retry to recover it. A named component is replaced
    /// wholesale, not merged into recursively. Byte-identity governs the epoch (ADR-EC1): a merge
    /// that changes nothing writes nothing and reports the epoch the document is already on.
    /// </para>
    /// <para>
    /// <see cref="EngagementContextMerge.ApplyThroughAsync"/> implements these semantics over this
    /// interface's other members, so an implementation may delegate to it.
    /// </para>
    /// </summary>
    /// <param name="engagementId">The engagement ID (partition key).</param>
    /// <param name="components">Component key → that component's rendered canonical JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The epoch the document is on after the merge (0-based).</returns>
    Task<int> MergeDynamicContextAsync(EngagementId engagementId, IReadOnlyDictionary<string, string> components, CancellationToken ct);

    /// <summary>
    /// Retrieve one version of an engagement's dynamic context with its provenance (S13.60,
    /// doc 04 §4 step 3): the current version when <paramref name="epoch"/> is
    /// <see langword="null"/>, otherwise exactly that epoch — so a run pinned to an epoch reads
    /// the bytes it was pinned to, never whatever is current.
    /// </summary>
    /// <param name="engagementId">The engagement ID (partition key).</param>
    /// <param name="epoch">The epoch to read, or <see langword="null"/> for the current one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version, or <see langword="null"/> if the engagement has no context, or has no such epoch.</returns>
    Task<EngagementContextSnapshot?> GetDynamicContextSnapshotAsync(EngagementId engagementId, int? epoch, CancellationToken ct);
}
