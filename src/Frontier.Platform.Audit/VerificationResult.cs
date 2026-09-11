using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// The result of re-verifying a <see cref="SignedAuditRecord"/>'s signature and chain
/// (doc 05 §2 <c>IAuditSigner.VerifyAsync</c>, §10 <c>POST /api/audit/{executionId}/verify</c>).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record VerificationResult
{
    /// <summary>Whether the record's <c>Signature</c> matches a recomputed HMAC over its <c>RecordHash</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("signature_valid")]
    public required bool SignatureValid { get; init; }

    /// <summary>Whether the engagement's hash chain is unbroken back to genesis.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("chain_valid")]
    public required bool ChainValid { get; init; }

    /// <summary>The execution id of the first record where the chain breaks, if <see cref="ChainValid"/> is <see langword="false"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("broken_link_at")]
    public string? BrokenLinkAt { get; init; }

    /// <summary>
    /// The signing key id (Key Vault key version) the <em>verified record itself</em> was signed
    /// under — that is, the target record's <see cref="SignedAuditRecord.SigningKeyId"/>, which is
    /// the version its signature was checked against. It is <em>not</em> "the current key":
    /// after a rotation a pre-rotation record still reports the older version here, forever
    /// (doc 05 §5; ADR-PA22 records the redefinition).
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("verified_against_key_id")]
    public required string VerifiedAgainstKeyId { get; init; }

    /// <summary>
    /// The distinct signing key ids appearing <em>anywhere in the verified chain</em> that could
    /// not be resolved to a key version, in chain order; <see langword="null"/> when every id
    /// resolved. A record whose key id is listed here fails signature verification (fail closed),
    /// but being listed distinguishes "the key version is gone" from "the signature is forged".
    /// The chain's hash continuity needs no key and is still reported through
    /// <see cref="ChainValid"/> and <see cref="BrokenLinkAt"/> (ADR-PA22).
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("unresolved_key_ids")]
    public IReadOnlyList<string>? UnresolvedKeyIds { get; init; }
}
