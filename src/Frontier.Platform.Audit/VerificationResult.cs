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

    /// <summary>
    /// Where verification stopped trusting the chain: the first record on the walk whose signature
    /// failed against an available key, or failing that the first record the hash links could not
    /// reach. <see langword="null"/> when the chain is whole.
    ///
    /// <para>
    /// Its meaning for a genuine link break is unchanged — a record whose <c>previous_record_hash</c>
    /// points at nothing still names itself here. What changed (ADR-PA31) is that concurrent closes
    /// no longer produce one: verification follows the hash links rather than the stored
    /// <c>closed_at_utc</c> order, so a chain written out of timestamp order by the head race
    /// verifies clean instead of reporting a false break here.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Every place two or more records in the chain claim the same <c>previous_record_hash</c>, in
    /// chain order; <see langword="null"/> when there are none (ADR-PA31). A fork makes
    /// <see cref="ChainValid"/> false — it is a real discontinuity and hiding it would be dishonest —
    /// but it is reported here, on its own terms, rather than as a signature mismatch: each forked
    /// record's signature verifies perfectly well against its own key. A
    /// <see cref="AuditChainForkKind.Legacy"/> fork is the S13.106 defect's own footprint, not
    /// tampering.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("forks")]
    public IReadOnlyList<AuditChainFork>? Forks { get; init; }

    /// <summary>
    /// The execution ids of stored records the hash links could not reach from genesis, in stored
    /// order; <see langword="null"/> when every record lies on the chain (ADR-PA31). It is its own
    /// finding, distinct from a signature mismatch and from a fork: the records themselves may be
    /// perfectly well signed, but something before them is missing, altered or branched, so they no
    /// longer hang off genesis. Where a fork put two records on one predecessor, the arm the walk did
    /// not take appears here beside the fork that explains it.
    /// </summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("unreachable_records")]
    public IReadOnlyList<string>? UnreachableRecords { get; init; }
}
