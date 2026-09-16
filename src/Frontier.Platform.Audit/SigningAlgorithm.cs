using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// How an audit record's <c>signature</c> field was produced (ADR-PA33). The algorithm is carried
/// by the resolved <see cref="SigningKey"/>, never by the record: the record's
/// <c>signing_key_id</c> already names exactly one key version, and that version has exactly one
/// algorithm, so adding a field to the signed shape would have changed the canonical bytes of
/// every record ever written for no information gain (ADR-PA33 decision 3).
/// </summary>
public sealed class SigningAlgorithm : SmartEnum<SigningAlgorithm>
{
    /// <summary>
    /// <c>HMAC-SHA256(UTF8(record_hash), key)</c>, hex-encoded — the algorithm every record written
    /// before ADR-PA33 used, and the one the local <c>dev-key/*</c> keys still use. Retained for
    /// verification forever (ADR-PA22); no longer used for signing outside the local profile.
    /// </summary>
    public static readonly SigningAlgorithm HmacSha256 = new("hmac_sha256");

    /// <summary>
    /// ECDSA over NIST P-256 with SHA-256 (JWA <c>ES256</c>, RFC 7518), signed inside Key Vault and
    /// verified locally against the key version's public part. The signed payload is unchanged —
    /// still <c>UTF8(record_hash)</c> — so the hash chain is identical under either algorithm.
    /// </summary>
    public static readonly SigningAlgorithm Es256 = new("es256");

    /// <remarks>
    /// <c>internal</c> rather than <c>private</c> so the tests can construct an algorithm this build
    /// does not implement and prove that verification fails closed on it. No public surface changes:
    /// a consumer still cannot add a value, and only the two declared above appear in
    /// <see cref="Frontier.Platform.Abstractions.SmartEnum{TEnum}.List"/>.
    /// </remarks>
    internal SigningAlgorithm(string name)
        : base(name)
    {
    }
}
