using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>
/// What verifying an engagement's audit chain concluded (consumer S13.334 B1, ADR-PA22 decision C as revised
/// 2026-11-25). Serializes as a snake_case string. Only <see cref="Valid"/> means the chain is proven: every
/// record on it was checked against a key version the deployment still holds.
/// </summary>
public sealed class AuditChainVerdict : SmartEnum<AuditChainVerdict>
{
    /// <summary>The chain is whole and every record's signature verified against its own key version.</summary>
    public static readonly AuditChainVerdict Valid = new("valid");

    /// <summary>
    /// There is evidence of damage: a signature or recomputed hash that does not match, a broken or
    /// unreachable link, a fork, or a head that the walk does not reach.
    /// </summary>
    public static readonly AuditChainVerdict Broken = new("broken");

    /// <summary>
    /// No damage was found, but at least one record names a key version that could not be resolved, so its
    /// signature was never checked. This is not benign: a store writer can turn a broken chain into an
    /// unverified one by giving records bogus key ids. Alert on it.
    /// </summary>
    public static readonly AuditChainVerdict Unverified = new("unverified");

    private AuditChainVerdict(string name)
        : base(name)
    {
    }
}

/// <summary>
/// The engagement's chain head names a record the walk from genesis does not reach where the head says it should
/// (consumer S13.334 B2): the newest records were removed, or the head was edited. The head is mutable, unsigned
/// bookkeeping, so a writer who also rewrites it defeats this check; completeness needs an external anchor.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by AuditChainVerifier tests.")]
public sealed record AuditChainHeadMismatch
{
    /// <summary>The record count the head claims.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("head_sequence")]
    public required long HeadSequence { get; init; }

    /// <summary>The record hash the head names.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("head_record_hash")]
    public required string HeadRecordHash { get; init; }

    /// <summary>How many records the walk from genesis reached.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("walked_count")]
    public required int WalkedCount { get; init; }

    /// <summary>The record hash at the head's position on the walk, or the walk's last one when it is shorter; null when nothing was walked.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("walked_record_hash")]
    public string? WalkedRecordHash { get; init; }
}
