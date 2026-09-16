using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Audit;

/// <summary>
/// A versioned key for <em>verifying</em> audit-record signatures (doc 05 §5). The
/// <see cref="KeyId"/> is recorded on each signed record as <c>SigningKeyId</c> so a later key
/// rotation never invalidates existing signatures.
///
/// <para>
/// <see cref="KeyMaterial"/> is whatever verifying under <see cref="Algorithm"/> needs, and what
/// that is differs by algorithm in the way that matters most (ADR-PA33):
/// for <see cref="SigningAlgorithm.HmacSha256"/> it is the <em>secret</em> HMAC key, which is why
/// that algorithm is confined to the local profile; for <see cref="SigningAlgorithm.Es256"/> it is
/// the key version's <em>public</em> part as a DER SubjectPublicKeyInfo — public by construction,
/// safe to cache in memory, safe to log, and useless for signing. Under ES256 the private key never
/// leaves Key Vault, which is doc 05 §5's actual requirement.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Record/POCO with assignment-only constructor")]
public sealed record SigningKey(string KeyId, ReadOnlyMemory<byte> KeyMaterial)
{
    /// <summary>
    /// How signatures made under this key version are verified. Defaults to
    /// <see cref="SigningAlgorithm.HmacSha256"/> so the positional constructor keeps the exact
    /// meaning it had before ADR-PA33 — every existing caller and every stored <c>dev-key/*</c>
    /// record continues to resolve to an HMAC key with no change.
    /// </summary>
    public SigningAlgorithm Algorithm { get; init; } = SigningAlgorithm.HmacSha256;

    /// <summary>
    /// An <see cref="SigningAlgorithm.Es256"/> key version: <paramref name="keyId"/> is the full
    /// versioned Key Vault key identifier and <paramref name="publicKeyDer"/> its public part as a
    /// DER SubjectPublicKeyInfo.
    /// </summary>
    public static SigningKey ForEs256(string keyId, ReadOnlyMemory<byte> publicKeyDer) =>
        new(keyId, publicKeyDer) { Algorithm = SigningAlgorithm.Es256 };
}
