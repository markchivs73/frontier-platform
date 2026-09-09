namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// One version of an engagement's dynamic context as the store holds it (doc 04 §6): the
/// append-only epoch document's number, id, content hash and content. Returned by
/// <see cref="IEngagementContextStore.GetDynamicContextSnapshotAsync"/> so an assembly can cite
/// <em>which</em> version it read (doc 04 §4 step 3 — <c>DynamicTier(engagementId, snapshot.Epoch,
/// snapshot.Ref, …)</c>) rather than only its bytes.
/// </summary>
/// <param name="Epoch">The store's epoch number, 0-based; the pin a run carries (S13.60).</param>
/// <param name="Ref">The epoch document id (<c>{engagementId}:ctx:e{epoch:D6}</c>) — <see cref="Frontier.Platform.Serialization.DynamicTier.AssembledFromSnapshotRef"/>.</param>
/// <param name="ContentHash">The canonical hash of <paramref name="Content"/> as the store recorded it — the run's <c>dynamic_context_hash</c>.</param>
/// <param name="Content">The dynamic context JSON.</param>
public sealed record EngagementContextSnapshot(int Epoch, string Ref, string ContentHash, string Content);
