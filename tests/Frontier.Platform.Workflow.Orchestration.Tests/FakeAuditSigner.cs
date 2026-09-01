using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Deterministic <see cref="IAuditSigner"/> test double for <see cref="ConsolidateAuditActivityTests"/>
/// (S5.6): chains/signs via <see cref="AuditFixtures.SignedRecord(AuditRecord)"/>'s fixed
/// fixture values, and records every <see cref="AuditRecord"/> it was asked to sign.
/// <see cref="VerifyAsync"/> is not exercised by <see cref="ConsolidateAuditActivity"/>.
/// </summary>
internal sealed class FakeAuditSigner : IAuditSigner
{
    /// <summary>Every <see cref="AuditRecord"/> passed to <see cref="SignAsync"/>, in call order.</summary>
    public List<AuditRecord> SignedRecords { get; } = [];

    /// <inheritdoc />
    public Task<SignedAuditRecord> SignAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        SignedRecords.Add(record);
        return Task.FromResult(AuditFixtures.SignedRecord(record));
    }

    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(string executionId, string engagementId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not exercised by ConsolidateAuditActivity (S5.6).");
}
