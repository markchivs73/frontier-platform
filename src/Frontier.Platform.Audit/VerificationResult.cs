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

    /// <summary>The signing key id (Key Vault key version) verification was performed against.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("verified_against_key_id")]
    public required string VerifiedAgainstKeyId { get; init; }
}
