using System.Security.Cryptography;
using System.Text;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Canonical-byte helpers for <see cref="SignedAuditRecord"/>'s hash chain (doc 05 §5):
/// <see cref="ComputeRecordHash"/> hashes an <see cref="AuditRecord"/> plus the chain's
/// previous hash, mirroring <c>DefinitionHasher</c>'s "clear the hash field, hash the rest"
/// pattern (canonical-serialization) by serializing the <see cref="SignedAuditRecord"/>
/// shape with <c>record_hash</c>/<c>signature</c>/<c>signing_key_id</c> cleared.
/// </summary>
internal static class AuditRecordHasher
{
    /// <summary>The hex SHA-256 digest of <paramref name="record"/> chained from <paramref name="previousRecordHash"/> (doc 05 §5 fields 0-14).</summary>
    internal static string ComputeRecordHash(AuditRecord record, string previousRecordHash) =>
        Convert.ToHexString(SHA256.HashData(GetCanonicalBytes(record, previousRecordHash)));

    /// <summary>The canonical bytes <see cref="ComputeRecordHash"/> hashes.</summary>
    internal static byte[] GetCanonicalBytes(AuditRecord record, string previousRecordHash) =>
        CanonicalProfile.SerializeCanonical(ToSignedShape(record, previousRecordHash, recordHash: string.Empty, signature: string.Empty, signingKeyId: string.Empty));

    /// <summary>Genesis hash for an engagement with no prior <c>audit-records</c> entry (doc 05 §5): <c>SHA-256(engagementId)</c>.</summary>
    internal static string ComputeGenesisHash(string engagementId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engagementId)));

    /// <summary>HMAC-SHA256(<paramref name="recordHash"/>, <paramref name="keyMaterial"/>), hex-encoded (doc 05 §5).</summary>
    internal static string ComputeSignature(string recordHash, ReadOnlyMemory<byte> keyMaterial) =>
        Convert.ToHexString(HMACSHA256.HashData(keyMaterial.Span, Encoding.UTF8.GetBytes(recordHash)));

    /// <summary>Copies <paramref name="record"/>'s fields 0-13 into a <see cref="SignedAuditRecord"/> with the supplied chain/signature fields 14-17.</summary>
    internal static SignedAuditRecord ToSignedShape(AuditRecord record, string previousRecordHash, string recordHash, string signature, string signingKeyId) => new()
    {
        SchemaVersion = record.SchemaVersion,
        ExecutionId = record.ExecutionId,
        EngagementId = record.EngagementId,
        WorkflowId = record.WorkflowId,
        DefinitionVersion = record.DefinitionVersion,
        DefinitionHash = record.DefinitionHash,
        StartedAtUtc = record.StartedAtUtc,
        ClosedAtUtc = record.ClosedAtUtc,
        FinalStatus = record.FinalStatus,
        OrchestrationEvents = record.OrchestrationEvents,
        AgentInvocations = record.AgentInvocations,
        ValidatorOutcomes = record.ValidatorOutcomes,
        HumanDecisions = record.HumanDecisions,
        CacheMetrics = record.CacheMetrics,
        PreviousRecordHash = previousRecordHash,
        RecordHash = recordHash,
        Signature = signature,
        SigningKeyId = signingKeyId,
    };

    /// <summary>Projects a persisted <see cref="SignedAuditRecord"/> back to its <see cref="AuditRecord"/> fields 0-13, for re-hashing on verify.</summary>
    internal static AuditRecord ToAuditRecord(SignedAuditRecord record) => new()
    {
        SchemaVersion = record.SchemaVersion,
        ExecutionId = record.ExecutionId,
        EngagementId = record.EngagementId,
        WorkflowId = record.WorkflowId,
        DefinitionVersion = record.DefinitionVersion,
        DefinitionHash = record.DefinitionHash,
        StartedAtUtc = record.StartedAtUtc,
        ClosedAtUtc = record.ClosedAtUtc,
        FinalStatus = record.FinalStatus,
        OrchestrationEvents = record.OrchestrationEvents,
        AgentInvocations = record.AgentInvocations,
        ValidatorOutcomes = record.ValidatorOutcomes,
        HumanDecisions = record.HumanDecisions,
        CacheMetrics = record.CacheMetrics,
    };
}
