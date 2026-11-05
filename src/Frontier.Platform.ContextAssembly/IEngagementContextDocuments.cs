using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// The storage half of <see cref="CosmosEngagementContextStore"/> (doc 04 §6): point reads and writes of the
/// <c>engagement-context</c> pointer and epoch documents, with the SDK's status codes already translated, so
/// epoch numbering and the lost-race handling stay unit-testable (S13.193).
/// </summary>
internal interface IEngagementContextDocuments
{
    /// <summary>The engagement's <c>:current</c> pointer; <see langword="null"/> when it has no context.</summary>
    Task<EngagementContextPointer?> ReadPointerAsync(EngagementId engagementId, CancellationToken ct);

    /// <summary>The epoch document <paramref name="epochId"/>; <see langword="null"/> when it is not stored.</summary>
    Task<EngagementContextEpoch?> ReadEpochAsync(EngagementId engagementId, string epochId, CancellationToken ct);

    /// <summary>Appends <paramref name="epoch"/>; <see langword="false"/> when a concurrent writer already created that epoch.</summary>
    Task<bool> TryCreateEpochAsync(EngagementContextEpoch epoch, CancellationToken ct);

    /// <summary>Creates or moves the <c>:current</c> pointer.</summary>
    Task UpsertPointerAsync(EngagementContextPointer pointer, CancellationToken ct);
}
