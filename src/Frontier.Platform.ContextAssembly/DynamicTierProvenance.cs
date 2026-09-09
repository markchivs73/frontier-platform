using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Where a dynamic tier's bytes came from (doc 04 §4 step 3): the engagement, the epoch that
/// was read, and the epoch document it was read from. Passed to
/// <see cref="IContextAssembler.AssembleAsync"/> so <see cref="Frontier.Platform.Serialization.DynamicTier"/>
/// carries real provenance instead of placeholders (S13.60). <see langword="null"/> means the
/// caller had no context to cite — an unpinned assembly with nothing stored.
/// </summary>
/// <param name="EngagementId">The engagement the context belongs to.</param>
/// <param name="Epoch">The epoch read.</param>
/// <param name="AssembledFromSnapshotRef">The epoch document id read.</param>
public sealed record DynamicTierProvenance(EngagementId EngagementId, int Epoch, string AssembledFromSnapshotRef);
