using System.Text;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

/// <summary>S5.5 tests for <see cref="AuditRecordHasher"/> (doc 05 §5).</summary>
public sealed class AuditRecordHasherTests
{
    [Fact]
    public void ComputeRecordHash_SameInputsTwice_IsDeterministic()
    {
        var record = Sample();

        var first = AuditRecordHasher.ComputeRecordHash(record, "previous-hash");
        var second = AuditRecordHasher.ComputeRecordHash(record, "previous-hash");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeRecordHash_DifferentPreviousHash_ProducesDifferentHash()
    {
        var record = Sample();

        var first = AuditRecordHasher.ComputeRecordHash(record, "previous-hash-a");
        var second = AuditRecordHasher.ComputeRecordHash(record, "previous-hash-b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ComputeRecordHash_DifferentContent_ProducesDifferentHash()
    {
        var record = Sample();
        var changed = record with { WorkflowId = "wf-2" };

        Assert.NotEqual(
            AuditRecordHasher.ComputeRecordHash(record, "previous-hash"),
            AuditRecordHasher.ComputeRecordHash(changed, "previous-hash"));
    }

    [Fact]
    public void GetCanonicalBytes_ClearsHashSignatureAndKeyIdAndSetsPreviousHash()
    {
        var bytes = AuditRecordHasher.GetCanonicalBytes(Sample(), "previous-hash");
        var json = Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"record_hash\":\"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"signature\":\"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"signing_key_id\":\"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"previous_record_hash\":\"previous-hash\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeGenesisHash_SameEngagementTwice_IsDeterministic()
    {
        Assert.Equal(AuditRecordHasher.ComputeGenesisHash("eng-1"), AuditRecordHasher.ComputeGenesisHash("eng-1"));
    }

    [Fact]
    public void ComputeGenesisHash_DifferentEngagements_ProducesDifferentHash()
    {
        Assert.NotEqual(AuditRecordHasher.ComputeGenesisHash("eng-1"), AuditRecordHasher.ComputeGenesisHash("eng-2"));
    }

    [Fact]
    public void ComputeSignature_SameInputsTwice_IsDeterministic()
    {
        var keyMaterial = Encoding.UTF8.GetBytes("signing-key");

        var first = AuditRecordHasher.ComputeSignature("record-hash", keyMaterial);
        var second = AuditRecordHasher.ComputeSignature("record-hash", keyMaterial);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeSignature_DifferentKeyMaterial_ProducesDifferentSignature()
    {
        var first = AuditRecordHasher.ComputeSignature("record-hash", Encoding.UTF8.GetBytes("key-a"));
        var second = AuditRecordHasher.ComputeSignature("record-hash", Encoding.UTF8.GetBytes("key-b"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToSignedShape_CopiesFields0Through13AndSetsChainAndSignatureFields()
    {
        var record = Sample();

        var signed = AuditRecordHasher.ToSignedShape(record, "previous-hash", "record-hash", "signature", "key-id");

        Assert.Equal(record.SchemaVersion, signed.SchemaVersion);
        Assert.Equal(record.ExecutionId, signed.ExecutionId);
        Assert.Equal(record.EngagementId, signed.EngagementId);
        Assert.Equal(record.WorkflowId, signed.WorkflowId);
        Assert.Equal(record.DefinitionVersion, signed.DefinitionVersion);
        Assert.Equal(record.DefinitionHash, signed.DefinitionHash);
        Assert.Equal(record.StartedAtUtc, signed.StartedAtUtc);
        Assert.Equal(record.ClosedAtUtc, signed.ClosedAtUtc);
        Assert.Equal(record.FinalStatus, signed.FinalStatus);
        Assert.Same(record.OrchestrationEvents, signed.OrchestrationEvents);
        Assert.Same(record.AgentInvocations, signed.AgentInvocations);
        Assert.Same(record.ValidatorOutcomes, signed.ValidatorOutcomes);
        Assert.Same(record.HumanDecisions, signed.HumanDecisions);
        Assert.Same(record.CacheMetrics, signed.CacheMetrics);
        Assert.Equal("previous-hash", signed.PreviousRecordHash);
        Assert.Equal("record-hash", signed.RecordHash);
        Assert.Equal("signature", signed.Signature);
        Assert.Equal("key-id", signed.SigningKeyId);
    }

    [Fact]
    public void ToAuditRecord_ProjectsFields0Through13()
    {
        var record = Sample();
        var signed = AuditRecordHasher.ToSignedShape(record, "previous-hash", "record-hash", "signature", "key-id");

        var projected = AuditRecordHasher.ToAuditRecord(signed);

        Assert.Equal(record, projected);
    }

    /// <summary>A well-formed <see cref="AuditRecord"/> (mirrors the S5.1 contract sample, doc 05 §3).</summary>
    internal static AuditRecord Sample() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        FinalStatus = ExecutionStatus.Completed,
        OrchestrationEvents =
        [
            new WorkflowEvent
            {
                EventType = WorkflowEventType.TaskCompleted,
                NodeId = "pricing-agent",
                CorrelationId = "corr-1",
                OccurredAtUtc = new DateTime(2026, 1, 1, 0, 25, 0, DateTimeKind.Utc),
                Details = "Pricing section produced.",
            },
        ],
        AgentInvocations =
        [
            new AgentInvocation
            {
                CorrelationId = "corr-1",
                NodeId = "pricing-agent",
                AgentRole = "deep-reasoning",
                ResolvedModel = new ResolvedModelSummary
                {
                    RoleId = "deep-reasoning",
                    Provider = "anthropic",
                    ModelId = "claude-fable-5",
                    ChainPosition = 0,
                    MappingVersion = 1,
                },
                InputContractType = "EngagementBriefSection",
                InputHash = "a1b2c3",
                OutputContractType = "PricingSection",
                OutputHash = "d4e5f6",
                InputTokens = 1200,
                OutputTokens = 450,
                CacheReadTokens = 800,
                CacheWriteTokens = 200,
                RetryCount = 0,
                LatencyMs = 2400,
                ToolCalls = [],
                InvokedAtUtc = new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc),
            },
        ],
        ValidatorOutcomes = [],
        HumanDecisions =
        [
            new HumanDecisionRecord
            {
                GateId = "human-gate",
                RequestId = "eng-1::wf-1:human-gate:1",
                ApproverId = "approver-1",
                Kind = DecisionKind.Approve,
                Notes = "Looks good.",
                DecidedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            },
        ],
        CacheMetrics = new CacheMetrics
        {
            Baseline = new CacheTierMetrics { Reads = 14, Writes = 1, HitRatePercent = 93.3m, TokensRead = 182000 },
            Dynamic = new CacheTierMetrics { Reads = 11, Writes = 2, HitRatePercent = 78.6m, TokensRead = 0 },
            RealTime = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 },
        },
    };
}
