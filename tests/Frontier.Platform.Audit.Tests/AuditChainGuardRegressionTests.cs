using System.Text.Json;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.106 / ADR-PA31 regression pins. The guard added a head document, a <c>doc_type</c> on the
/// document wrapper, and fork reporting — and changed <em>nothing</em> about how a record is hashed
/// or signed. These tests fail if that ever stops being true, which is the whole premise of shipping
/// the fix without a schema bump: every record already stored must keep verifying byte-for-byte.
/// </summary>
public sealed class AuditChainGuardRegressionTests
{
    /// <summary>The checked-in golden record every other audit golden test also pins.</summary>
    private static SignedAuditRecord Golden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "signed_audit_record.json");
        return JsonSerializer.Deserialize<SignedAuditRecord>(File.ReadAllBytes(path), CanonicalProfile.Options)!;
    }

    [Fact]
    public void SignedRecordBytes_AreUnchangedByTheDocumentWrapper()
    {
        // doc_type sits on the wrapper, outside `record`. If it ever leaked inside, every stored
        // signature would break — so this asserts the wrapped record still serializes to the exact
        // golden bytes.
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "signed_audit_record.json");
        var goldenBytes = File.ReadAllBytes(path);

        var document = SignedAuditRecordDocument.FromRecord(Golden());

        Assert.Equal(AuditRecordDocumentId.RecordDocType, document.DocType);
        Assert.Equal(goldenBytes, CanonicalProfile.SerializeCanonical(document.Record));
    }

    [Fact]
    public void RecordHash_OverTheGoldenRecord_IsUnchanged()
    {
        var golden = Golden();

        var recomputed = AuditRecordHasher.ComputeRecordHash(AuditRecordHasher.ToAuditRecord(golden), golden.PreviousRecordHash);

        Assert.Equal("9967F73A0B721EDC9AE5C896329D20F19FAA2C3A4913DD74D0F422D4CB0A827D", recomputed);
    }

    [Fact]
    public void Signature_OverTheGoldenRecord_IsUnchanged()
    {
        var golden = Golden();
        var recordHash = AuditRecordHasher.ComputeRecordHash(AuditRecordHasher.ToAuditRecord(golden), golden.PreviousRecordHash);

        var signature = AuditRecordHasher.ComputeSignature(recordHash, FakeRotatingKeyProvider.V1.KeyMaterial);

        Assert.Equal("EE39E73AF91F7BC07B3EE796D9A147A8BC4D75D09B565A58ED4E5B07FFBC3B79", signature);
    }

    [Fact]
    public void GenesisHash_IsUnchanged() =>
        Assert.Equal("CF2640FFE69EAFB0001F0970DBC6E6D19637F8E0EB53F0D120E5C56A9A2D1F73", AuditRecordHasher.ComputeGenesisHash("eng-1"));
}
