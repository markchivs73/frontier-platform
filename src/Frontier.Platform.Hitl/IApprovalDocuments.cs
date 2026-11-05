namespace Frontier.Platform.Hitl;

/// <summary>
/// The version-checked read and write <see cref="ApprovalDecider"/> runs its rounds over (doc 06 §9):
/// the storage half of <see cref="IApprovalStore.DecideAsync"/>, with the SDK's status codes already
/// translated so the decision loop itself carries no SDK type and is unit-testable (S13.193).
/// </summary>
internal interface IApprovalDocuments
{
    /// <summary>Point-reads the request with the version it was read at; <see langword="null"/> when the id is unknown.</summary>
    Task<VersionedApproval?> ReadAsync(string engagementId, string approvalId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the stored request only if it is still at <paramref name="expectedVersion"/>;
    /// <see langword="false"/> when another write landed first.
    /// </summary>
    Task<bool> TryReplaceAsync(ApprovalRequest request, string expectedVersion, CancellationToken cancellationToken);
}

/// <summary>An <see cref="ApprovalRequest"/> as read, with the concurrency token (Cosmos <c>_etag</c>) it was read at.</summary>
/// <param name="Request">The stored request.</param>
/// <param name="Version">The token a replace must match to win.</param>
internal sealed record VersionedApproval(ApprovalRequest Request, string Version);
