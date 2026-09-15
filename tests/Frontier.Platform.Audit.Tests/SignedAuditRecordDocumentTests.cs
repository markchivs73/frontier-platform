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

    [Theory]
    [InlineData(null, -1)]
    [InlineData(false, -1)]
    [InlineData(true, 604800)]
    public async Task FromRecord_ExpiresOnlySandboxRecords_AfterSevenDays(bool? sandbox, int expectedTtl)
    {
        var key = await new DevKeyProvider().GetCurrentKeyAsync(CancellationToken.None);
        var record = AuditRecordHasher.ToSignedShape(
            AuditRecordHasherTests.Sample(),
            previousRecordHash: AuditRecordHasher.ComputeGenesisHash("eng-1"),
            recordHash: "record-hash",
            signature: "signature",
            signingKeyId: key.KeyId) with { Sandbox = sandbox };

        var document = SignedAuditRecordDocument.FromRecord(record);

        // Doc 13 §5: sandbox records purge after ~7 days; a real run's governance record must never expire.
        Assert.Equal(expectedTtl, document.Ttl);
        var json = System.Text.Json.JsonSerializer.Serialize(document, Frontier.Platform.Serialization.CanonicalProfile.Options);
        Assert.Contains($"\"ttl\":{expectedTtl}", json, StringComparison.Ordinal);
    }
}
