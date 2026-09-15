using System.Security.Cryptography;
using System.Text;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Canonical-byte helpers for the governance chain (ADR-PA30). The typed shell is written by the
/// canonical profile and the whole document is then put through RFC 8785 JCS, so untyped change
/// content hashes the same whatever its member order (ADR-E2 decision 2). Every digest is
/// domain-separated from the execution chain's.
/// </summary>
internal static class GovernanceAuditHasher
{
    /// <summary>The genesis domain: a governance genesis is never an execution genesis, which hashes the bare engagement id.</summary>
    internal const string GenesisDomain = "governance:";

    /// <summary>The record-id domain.</summary>
    internal const string RecordIdDomain = "governance-entry:";

    /// <summary><c>SHA-256("governance:" + scope)</c>, hex: the previous hash of a scope's first record.</summary>
    internal static string ComputeGenesisHash(string scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(GenesisDomain + scope)));

    /// <summary>The deterministic record id for <paramref name="entry"/>: SHA-256 over the domain and the entry's JCS bytes.</summary>
    internal static string ComputeRecordId(GovernanceAuditEntry entry) =>
        Convert.ToHexString(SHA256.HashData([.. Encoding.UTF8.GetBytes(RecordIdDomain), .. ToJcs(entry)]));

    /// <summary>The bytes <see cref="ComputeRecordHash"/> hashes: <paramref name="record"/> with its hash and signature empty, JCS-canonicalised.</summary>
    internal static byte[] GetCanonicalBytes(SignedGovernanceAuditRecord record) =>
        ToJcs(record with { RecordHash = string.Empty, Signature = string.Empty });

    /// <summary>The hex SHA-256 of <see cref="GetCanonicalBytes"/>.</summary>
    internal static string ComputeRecordHash(SignedGovernanceAuditRecord record) =>
        Convert.ToHexString(SHA256.HashData(GetCanonicalBytes(record)));

    /// <summary>Allocates <paramref name="entry"/> at <paramref name="sequence"/> after <paramref name="previousRecordHash"/>, and hashes and signs it with <paramref name="key"/>.</summary>
    internal static SignedGovernanceAuditRecord Seal(GovernanceAuditEntry entry, string recordId, long sequence, string previousRecordHash, SigningKey key)
    {
        var unsigned = ToUnsignedRecord(entry, recordId, sequence, previousRecordHash, key.KeyId);
        var recordHash = ComputeRecordHash(unsigned);

        return unsigned with
        {
            RecordHash = recordHash,
            Signature = AuditRecordHasher.ComputeSignature(recordHash, key.KeyMaterial),
        };
    }

    /// <summary>Projects a stored record back to the entry it was made from.</summary>
    internal static GovernanceAuditEntry ToEntry(SignedGovernanceAuditRecord record) => new()
    {
        Scope = record.Scope,
        EventType = record.EventType,
        SubjectType = record.SubjectType,
        SubjectId = record.SubjectId,
        SubjectVersion = record.SubjectVersion,
        Actor = record.Actor,
        ActorUpn = record.ActorUpn,
        Reason = record.Reason,
        OccurredAtUtc = record.OccurredAtUtc,
        CorrelationId = record.CorrelationId,
        EngagementId = record.EngagementId,
        Change = record.Change,
        BeforeHash = record.BeforeHash,
        AfterHash = record.AfterHash,
        CompensatesRecordId = record.CompensatesRecordId,
    };

    private static SignedGovernanceAuditRecord ToUnsignedRecord(GovernanceAuditEntry entry, string recordId, long sequence, string previousRecordHash, string signingKeyId) => new()
    {
        RecordId = recordId,
        Scope = entry.Scope,
        Sequence = sequence,
        EventType = entry.EventType,
        SubjectType = entry.SubjectType,
        SubjectId = entry.SubjectId,
        SubjectVersion = entry.SubjectVersion,
        Actor = entry.Actor,
        ActorUpn = entry.ActorUpn,
        Reason = entry.Reason,
        OccurredAtUtc = entry.OccurredAtUtc,
        CorrelationId = entry.CorrelationId,
        EngagementId = entry.EngagementId,
        Change = entry.Change,
        BeforeHash = entry.BeforeHash,
        AfterHash = entry.AfterHash,
        CompensatesRecordId = entry.CompensatesRecordId,
        PreviousRecordHash = previousRecordHash,
        RecordHash = string.Empty,
        Signature = string.Empty,
        SigningKeyId = signingKeyId,
    };

    private static byte[] ToJcs<T>(T value) =>
        JsonCanonicalizer.Canonicalize(CanonicalProfile.SerializeCanonical(value));
}
