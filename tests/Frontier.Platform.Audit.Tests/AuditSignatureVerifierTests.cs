using System.Security.Cryptography;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// S13.107 tests for <see cref="AuditSignatureVerifier"/> (ADR-PA33): the two algorithms, and the
/// fail-closed behaviour that every malformed input shares.
/// </summary>
public sealed class AuditSignatureVerifierTests
{
    private const string RecordHash = "3F786850E387550FDAB836ED7E6DC881DE23001B";

    [Fact]
    public void Verify_HmacSignature_VerifiesUnderTheHmacKey()
    {
        var key = new SigningKey("dev-key/v1", "frontier-dev-signing-key-v1"u8.ToArray());

        Assert.True(AuditSignatureVerifier.Verify(RecordHash, AuditSignatureVerifier.SignHmac(RecordHash, key.KeyMaterial), key));
    }

    [Fact]
    public void Verify_HmacSignatureUnderADifferentKey_ReturnsFalse()
    {
        var signature = AuditSignatureVerifier.SignHmac(RecordHash, "frontier-dev-signing-key-v1"u8.ToArray());
        var otherKey = new SigningKey("dev-key/v2", "frontier-dev-signing-key-v2"u8.ToArray());

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, signature, otherKey));
    }

    [Fact]
    public void Verify_Es256SignatureMadeWithThePrivateKey_VerifiesAgainstThePublicPartAlone()
    {
        // The claim option (c) rests on: verification needs only what is public, so it is local,
        // free, and available to someone with no access to the vault at all.
        var keyId = FakeKeyVaultSigningClient.V1;
        var signature = Convert.ToHexString(TestEs256Key.SignDigest(keyId, AuditSignatureVerifier.ComputeEs256Digest(RecordHash)));

        Assert.True(AuditSignatureVerifier.Verify(RecordHash, signature, TestEs256Key.VerificationKey(keyId)));
    }

    [Fact]
    public void Verify_Es256SignatureOverADifferentRecordHash_ReturnsFalse()
    {
        var keyId = FakeKeyVaultSigningClient.V1;
        var signature = Convert.ToHexString(TestEs256Key.SignDigest(keyId, AuditSignatureVerifier.ComputeEs256Digest("00")));

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, signature, TestEs256Key.VerificationKey(keyId)));
    }

    [Fact]
    public void Verify_Es256SignatureAgainstAnotherVersionsPublicKey_ReturnsFalse()
    {
        var signature = Convert.ToHexString(TestEs256Key.SignDigest(FakeKeyVaultSigningClient.V1, AuditSignatureVerifier.ComputeEs256Digest(RecordHash)));

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, signature, TestEs256Key.VerificationKey(FakeKeyVaultSigningClient.V2)));
    }

    [Fact]
    public void Verify_HmacSignatureOfferedAgainstAnEs256Key_ReturnsFalse()
    {
        // Algorithm confusion: a forger cannot get an HMAC accepted by naming an ES256 key version.
        var signature = AuditSignatureVerifier.SignHmac(RecordHash, "anything"u8.ToArray());

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, signature, TestEs256Key.VerificationKey(FakeKeyVaultSigningClient.V1)));
    }

    [Fact]
    public void Verify_MalformedHexSignature_ReturnsFalseRatherThanThrowing()
    {
        var key = new SigningKey("dev-key/v1", "material"u8.ToArray());

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "not-hex", key));
    }

    [Fact]
    public void Verify_EmptyHmacKeyMaterial_ReturnsFalse()
    {
        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "AB", new SigningKey("dev-key/v1", ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void Verify_EmptyEs256KeyMaterial_ReturnsFalse()
    {
        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "AB", SigningKey.ForEs256("kv/v1", ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void Verify_UnparseableEs256KeyMaterial_ReturnsFalse()
    {
        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "AB", SigningKey.ForEs256("kv/v1", "not-a-der-key"u8.ToArray())));
    }

    [Fact]
    public void Verify_Es256SignatureOfTheWrongLength_ReturnsFalse()
    {
        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "ABCD", TestEs256Key.VerificationKey(FakeKeyVaultSigningClient.V1)));
    }

    [Fact]
    public void Verify_KeyNamingAnAlgorithmThisBuildDoesNotImplement_FailsClosed()
    {
        // Forward compatibility, stated as a guarantee rather than assumed: if a future key version
        // ever names an algorithm this build cannot check, the record does not verify. The dangerous
        // alternative — falling through to HMAC — would let an unknown algorithm be "verified" by
        // the wrong primitive.
        var key = new SigningKey("kv/v1", "material"u8.ToArray()) { Algorithm = new SigningAlgorithm("rsa_pss_sha512") };

        Assert.False(AuditSignatureVerifier.Verify(RecordHash, "AB", key));
    }

    [Fact]
    public void TryDecodeHex_MalformedInput_ReturnsFalseWithNoBytes()
    {
        Assert.False(AuditSignatureVerifier.TryDecodeHex("zz", out var bytes));
        Assert.Empty(bytes);
    }

    [Fact]
    public void ComputeEs256Digest_IsTheSha256OfTheSignedPayload()
    {
        // Pins what the vault is asked to sign: the SHA-256 of UTF8(record_hash), so the signed
        // payload is identical to the HMAC path's and the chain is algorithm-independent.
        var expected = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(RecordHash));

        Assert.Equal(expected, AuditSignatureVerifier.ComputeEs256Digest(RecordHash));
    }
}
