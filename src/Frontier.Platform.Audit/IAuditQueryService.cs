
namespace Frontier.Platform.Audit;

/// <summary>
/// Governance and Empirical Design Validation queries over <c>audit-records</c>
/// (doc 05 §2, §7, §10, S5.7). Backs the four <c>/api/audit/*</c> endpoints: a single
/// record, a filtered governance list, and an engagement's full hash chain (the fourth,
/// <c>/verify</c>, is served by <see cref="IAuditSigner.VerifyAsync"/> directly).
/// </summary>
public interface IAuditQueryService
{
    /// <summary>
    /// Returns the full signed record for <paramref name="executionId"/>, or
    /// <see langword="null"/> if no record has been consolidated for it yet (doc 05 §10
    /// <c>GET /api/audit/{executionId}</c>). Answers doc 05 §7 queries 1 ("which model
    /// produced artifact X" — <see cref="SignedAuditRecord.AgentInvocations"/>), 2
    /// (validator outcomes — <see cref="SignedAuditRecord.ValidatorOutcomes"/>, always
    /// <c>[]</c> until Stage 6), 3 (<see cref="SignedAuditRecord.HumanDecisions"/>), and 5
    /// (<see cref="SignedAuditRecord.CacheMetrics"/>) directly from the returned record.
    /// </summary>
    Task<SignedAuditRecord?> GetAsync(string executionId, string engagementId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns an <see cref="AuditSummary"/> for every <c>audit-records</c> document
    /// matching <paramref name="query"/>'s filters (doc 05 §7 queries 1, 2, 4, 8; doc 05
    /// §10 <c>GET /api/audit/query</c>). An empty <see cref="AuditQuery"/> returns every
    /// record. This is a governance/Empirical-Design-Validation query and may be
    /// cross-partition (cosmos-conventions).
    /// </summary>
    Task<IReadOnlyList<AuditSummary>> QueryAsync(AuditQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every <see cref="SignedAuditRecord"/> for <paramref name="engagementId"/>,
    /// in hash-chain order (doc 05 §10 <c>GET /api/audit/engagement/{engagementId}/chain</c>).
    /// </summary>
    Task<IReadOnlyList<SignedAuditRecord>> GetChainAsync(string engagementId, CancellationToken cancellationToken);
}
