using System.Text.Json;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Audit.Tests;

/// <summary>Fixtures for the S13.103 governance audit tests (ADR-PA30).</summary>
internal static class GovernanceAuditSamples
{
    internal static readonly DateTime OccurredAt = new(2026, 9, 15, 10, 30, 0, 123, DateTimeKind.Utc);

    internal const string ChangeJson = """{"responsibilities":["budget-approval","cost-validation"],"display_name":"Finance Lead","limit":50000.5}""";

    internal static string Genesis => GovernanceAuditHasher.ComputeGenesisHash(GovernanceAuditScopes.Deployment);

    /// <summary>A valid entry with every optional field set.</summary>
    internal static GovernanceAuditEntry Entry(string subjectId = "finance-lead", string reason = "added cost-validation responsibility") => new()
    {
        EventType = "approver_role_updated",
        SubjectType = "approver_role",
        SubjectId = subjectId,
        SubjectVersion = "2",
        Actor = "user:oid-4f2a",
        ActorUpn = "mark@example.org",
        Reason = reason,
        OccurredAtUtc = OccurredAt,
        CorrelationId = "corr-7",
        EngagementId = "E2E::Acme::Admin-Website",
        Change = Change(ChangeJson),
        BeforeHash = "5D41402ABC4B2A76B9719D911017C592",
        AfterHash = "7D793037A0760186574B0282F2F435E7",
    };

    internal static TypedPayload Change(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new TypedPayload { SchemaRef = "schemas/approver-role-change/1.0", Payload = document.RootElement.Clone() };
    }

    internal static SignedGovernanceAuditRecord Seal(GovernanceAuditEntry entry, long sequence, string previousHash, SigningKey key) =>
        GovernanceAuditHasher.Seal(entry, GovernanceAuditHasher.ComputeRecordId(entry), sequence, previousHash, key);

    /// <summary>A correctly linked deployment chain whose n-th record is signed with <paramref name="keys"/>[n].</summary>
    internal static List<SignedGovernanceAuditRecord> Chain(params SigningKey[] keys)
    {
        var records = new List<SignedGovernanceAuditRecord>();
        var previous = Genesis;

        for (var index = 0; index < keys.Length; index++)
        {
            var record = Seal(Entry(subjectId: $"role-{index + 1}"), index + 1, previous, keys[index]);
            records.Add(record);
            previous = record.RecordHash;
        }

        return records;
    }

    internal static GovernanceAuditChainHead HeadOf(IReadOnlyList<SignedGovernanceAuditRecord> records) => new()
    {
        Scope = GovernanceAuditScopes.Deployment,
        Sequence = records[^1].Sequence,
        RecordHash = records[^1].RecordHash,
    };

    internal static Dictionary<string, SigningKey> Keys(params SigningKey[] keys) =>
        keys.ToDictionary(key => key.KeyId, StringComparer.Ordinal);
}
