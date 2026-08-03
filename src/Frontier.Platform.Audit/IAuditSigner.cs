
namespace Frontier.Platform.Audit;

/// <summary>
/// Chains, signs, and persists <see cref="AuditRecord"/>s, and re-verifies the result
/// (doc 05 §5, §2). <see cref="SignAsync"/> is the consolidator's next step
/// (<c>ConsolidateAuditActivity</c>, S5.6); <see cref="VerifyAsync"/> backs
/// <c>POST /api/audit/{executionId}/verify</c> (S5.7).
/// </summary>
public interface IAuditSigner
{
    /// <summary>
    /// Computes <paramref name="record"/>'s <c>RecordHash</c> chained from the engagement's
    /// most recent <c>audit-records</c> entry (or genesis), signs it with the current
    /// <see cref="IKeyProvider"/> key, persists the result (create-only, doc 05 §6), and
    /// returns it.
    /// </summary>
    Task<SignedAuditRecord> SignAsync(AuditRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Re-derives <paramref name="executionId"/>'s engagement chain from <c>audit-records</c>,
    /// recomputes each record's hash and signature, and reports whether the target record's
    /// signature is valid and the chain is unbroken back to genesis.
    /// </summary>
    Task<VerificationResult> VerifyAsync(string executionId, CancellationToken cancellationToken);
}
