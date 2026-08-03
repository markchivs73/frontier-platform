namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="SignedAuditRecordDocument"/> (doc 02 §3, doc 05 §6).</summary>
public sealed class SignedAuditRecordDocumentTests
{
    [Fact]
    public async Task FromRecord_SetsDeterministicIdAndPartitionKey()
    {
        var key = await new DevKeyProvider().GetCurrentKeyAsync(CancellationToken.None);
        var record = AuditRecordHasher.ToSignedShape(
            AuditRecordHasherTests.Sample(),
            previousRecordHash: AuditRecordHasher.ComputeGenesisHash("eng-1"),
            recordHash: "record-hash",
            signature: "signature",
            signingKeyId: key.KeyId);

        var document = SignedAuditRecordDocument.FromRecord(record);

        Assert.Equal("eng-1::wf-1:audit", document.Id);
        Assert.Equal(record.EngagementId, document.EngagementId);
        Assert.Same(record, document.Record);
    }
}
