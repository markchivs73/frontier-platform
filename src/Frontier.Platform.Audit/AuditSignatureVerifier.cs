using System.Security.Cryptography;
using System.Text;

namespace Frontier.Platform.Audit;

/// <summary>
/// Verifies one record's <c>signature</c> against the key version that made it (ADR-PA33). Both
/// chains share this, so execution and governance records can never disagree about what a valid
/// signature is.
///
/// <para>
/// Verification is <em>local</em> under every algorithm and never calls Key Vault. Under ES256 that
/// is the point of the design rather than an optimisation: the public part is enough to verify, so
/// an auditor re-verifying a 500-record chain makes zero vault calls and needs no vault grant at
/// all. Under HMAC it is unavoidable — the verifier must hold the secret — which is the asymmetry
/// ADR-PA33 rests on.
/// </para>
/// </summary>
internal static class AuditSignatureVerifier
{
    /// <summary>
    /// Whether <paramref name="signature"/> is a valid signature over <paramref name="recordHash"/>
    /// under <paramref name="key"/>. Every failure mode — an unknown algorithm, malformed hex,
    /// unusable key material, a signature of the wrong length — returns <see langword="false"/>
    /// rather than throwing: a caller verifying a stored chain is asking a question about evidence,
    /// and "this does not verify" is the honest answer to all of them (fail closed, ADR-PA22).
    /// </summary>
    internal static bool Verify(string recordHash, string signature, SigningKey key)
    {
        if (!TryDecodeHex(signature, out var signatureBytes))
        {
            return false;
        }

        var signedPayload = Encoding.UTF8.GetBytes(recordHash);

        return key.Algorithm.Name switch
        {
            "hmac_sha256" => VerifyHmac(signedPayload, signatureBytes, key.KeyMaterial),
            "es256" => VerifyEs256(signedPayload, signatureBytes, key.KeyMaterial),
            _ => false,
        };
    }

    /// <summary>The hex signature <see cref="Verify"/> expects for <paramref name="recordHash"/> under an HMAC <paramref name="keyMaterial"/>.</summary>
    internal static string SignHmac(string recordHash, ReadOnlyMemory<byte> keyMaterial) =>
        Convert.ToHexString(HMACSHA256.HashData(keyMaterial.Span, Encoding.UTF8.GetBytes(recordHash)));

    /// <summary>
    /// The digest a Key Vault ES256 sign operation takes for <paramref name="recordHash"/>. Key
    /// Vault signs a <em>hash</em>, never data, so the SHA-256 is computed here and the signed
    /// payload stays <c>UTF8(record_hash)</c> — identical to what the HMAC path signs.
    /// </summary>
    internal static byte[] ComputeEs256Digest(string recordHash) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(recordHash));

    /// <summary>Constant-time comparison of the recomputed HMAC against <paramref name="signature"/>.</summary>
    internal static bool VerifyHmac(byte[] signedPayload, byte[] signature, ReadOnlyMemory<byte> keyMaterial)
    {
        if (keyMaterial.IsEmpty)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(keyMaterial.Span, signedPayload), signature);
    }

    /// <summary>
    /// ECDSA P-256/SHA-256 verification against the DER SubjectPublicKeyInfo in
    /// <paramref name="publicKeyDer"/>. Key Vault returns ES256 signatures as raw <c>r ‖ s</c>
    /// (IEEE P1363), not DER, so the format is stated explicitly rather than defaulted.
    /// </summary>
    internal static bool VerifyEs256(byte[] signedPayload, byte[] signature, ReadOnlyMemory<byte> publicKeyDer)
    {
        if (publicKeyDer.IsEmpty)
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyDer.Span, out _);

            return ecdsa.VerifyData(signedPayload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // Unparseable or wrong-curve key material: the record does not verify under the version
            // it names, which is a finding, not an error to propagate to the caller.
            return false;
        }
    }

    /// <summary>Decodes a hex signature, treating a malformed one as a failed verification rather than an exception.</summary>
    internal static bool TryDecodeHex(string signature, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(signature);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
