
namespace Frontier.Platform.Audit;

/// <summary>
/// Persistence for the <c>audit-records</c> container (doc 02 §3, doc 05 §6):
/// append-only, partitioned by <c>/engagement_id</c>, one document per execution
/// (<c>{executionId}:audit</c>).
/// </summary>
public interface IAuditRecordStore
{
    /// <summary>
    /// Returns the <see cref="SignedAuditRecord"/> for <paramref name="executionId"/>, or
    /// <see langword="null"/> if no <c>{executionId}:audit</c> document exists yet
    /// (doc 05 §10 <c>GET /api/audit/{executionId}</c>). Point-read by the deterministic
    /// id and the engagement partition key derived from <paramref name="executionId"/>.
    /// </summary>
    Task<SignedAuditRecord?> GetAsync(string executionId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every <see cref="SignedAuditRecord"/> for <paramref name="engagementId"/>,
    /// ordered by <see cref="SignedAuditRecord.ClosedAtUtc"/> ascending — the engagement's
    /// hash chain in chain order (cosmos-conventions: single-partition query).
    /// </summary>
    Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists <paramref name="record"/> under its deterministic <c>{executionId}:audit</c>
    /// id. Throws if a record with that id already exists — <c>audit-records</c> is
    /// append-only (doc 05 §6); a retried sign is not expected to change a closed
    /// execution's record.
    /// </summary>
    Task CreateAsync(SignedAuditRecord record, CancellationToken cancellationToken);
}
