using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit;

/// <summary>The kinds of break <see cref="GovernanceAuditChainVerifier"/> reports (ADR-PA30). Serializes as a snake_case string.</summary>
public sealed class GovernanceAuditBreakKind : SmartEnum<GovernanceAuditBreakKind>
{
    /// <summary>The record's content no longer matches its hash, or its hash no longer matches its signature.</summary>
    public static readonly GovernanceAuditBreakKind SignatureMismatch = new("signature_mismatch");

    /// <summary>The record's sequence is ahead of the walk: a record before it is missing.</summary>
    public static readonly GovernanceAuditBreakKind SequenceGap = new("sequence_gap");

    /// <summary>The record's sequence is behind the walk: it is stored out of order or duplicated.</summary>
    public static readonly GovernanceAuditBreakKind OutOfOrder = new("out_of_order");

    /// <summary>The record's previous hash is not its predecessor's record hash (or the scope's genesis).</summary>
    public static readonly GovernanceAuditBreakKind HashLinkBreak = new("hash_link_break");

    /// <summary>The key version the record names cannot be resolved, so it cannot be verified (fail closed, ADR-PA22).</summary>
    public static readonly GovernanceAuditBreakKind UnresolvedKey = new("unresolved_key");

    /// <summary>The record belongs to a different scope from the chain being verified.</summary>
    public static readonly GovernanceAuditBreakKind ScopeMismatch = new("scope_mismatch");

    /// <summary>The chain head does not name the last stored record.</summary>
    public static readonly GovernanceAuditBreakKind HeadMismatch = new("head_mismatch");

    private GovernanceAuditBreakKind(string name)
        : base(name)
    {
    }
}

/// <summary>One break in a governance chain, at an exact position (ADR-PA30).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by GovernanceAuditChainVerifier tests.")]
public sealed record GovernanceAuditChainBreak
{
    /// <summary>The 1-based position in stored order. A head mismatch reports the last record's position (0 for an empty chain).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("position")]
    public required long Position { get; init; }

    /// <summary>What is wrong there.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("kind")]
    public required GovernanceAuditBreakKind Kind { get; init; }

    /// <summary>The record at that position, when the break concerns a record.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record_id")]
    public string? RecordId { get; init; }

    /// <summary>The sequence the record (or head) claims.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("recorded_sequence")]
    public long? RecordedSequence { get; init; }
}

/// <summary>The outcome of <see cref="IGovernanceAuditService.VerifyAsync"/> (ADR-PA30).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by GovernanceAuditChainVerifier tests.")]
public sealed record GovernanceAuditVerificationResult
{
    /// <summary>The scope verified.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>True only when there is no break of any kind, an unresolved key included.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    /// <summary>How many records were walked.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record_count")]
    public required int RecordCount { get; init; }

    /// <summary>The sequence the chain head claims, when there is a head.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("head_sequence")]
    public long? HeadSequence { get; init; }

    /// <summary>Every break, in walk order; <see langword="null"/> when the chain is valid.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("breaks")]
    public IReadOnlyList<GovernanceAuditChainBreak>? Breaks { get; init; }

    /// <summary>The distinct key ids that could not be resolved, in chain order; <see langword="null"/> when all resolved.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("unresolved_key_ids")]
    public IReadOnlyList<string>? UnresolvedKeyIds { get; init; }
}

/// <summary>A scope's chain head: the sequence and hash of its latest record (ADR-PA30).</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; exercised by GovernanceAuditChainVerifier tests.")]
public sealed record GovernanceAuditChainHead
{
    /// <summary>The scope.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>The latest record's sequence.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("sequence")]
    public required long Sequence { get; init; }

    /// <summary>The latest record's hash.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record_hash")]
    public required string RecordHash { get; init; }
}

/// <summary>The governance chain scopes the platform knows (ADR-PA30).</summary>
public static class GovernanceAuditScopes
{
    /// <summary>Phase 1's single scope: every governance change in the deployment.</summary>
    public const string Deployment = "deployment";
}
