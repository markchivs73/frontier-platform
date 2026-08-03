using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

public sealed class SignedAuditRecordTests
{
    [Fact]
    public void Validate_WellFormedRecord_DoesNotThrow()
    {
        var record = Record();

        record.Validate();
    }

    [Fact]
    public void Validate_DefinitionVersionBelowOne_Throws()
    {
        var record = Record() with { DefinitionVersion = 0 };

        var exception = Assert.Throws<ContractViolationException>(record.Validate);

        Assert.Contains("definition_version must be at least 1.", exception.Violations);
    }

    [Fact]
    public void Validate_ClosedBeforeStarted_Throws()
    {
        var record = Record() with
        {
            StartedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            ClosedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var exception = Assert.Throws<ContractViolationException>(record.Validate);

        Assert.Contains("closed_at_utc must not be before started_at_utc.", exception.Violations);
    }

    [Fact]
    public void Validate_AgentInvocationWithoutCorrelationId_Throws()
    {
        var record = Record() with { AgentInvocations = [Invocation() with { CorrelationId = " " }] };

        var exception = Assert.Throws<ContractViolationException>(record.Validate);

        Assert.Contains("every agent invocation must have a correlation_id.", exception.Violations);
    }

    [Fact]
    public void Validate_MissingChainFields_Throws()
    {
        var record = Record() with { PreviousRecordHash = " " };

        var exception = Assert.Throws<ContractViolationException>(record.Validate);

        Assert.Contains("previous_record_hash, record_hash, signature, and signing_key_id must all be present.", exception.Violations);
    }

    static SignedAuditRecord Record() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        FinalStatus = ExecutionStatus.Completed,
        OrchestrationEvents = [],
        AgentInvocations = [Invocation()],
        ValidatorOutcomes = [],
        HumanDecisions = [],
        CacheMetrics = new CacheMetrics
        {
            Baseline = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 },
            Dynamic = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 },
            RealTime = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 },
        },
        PreviousRecordHash = "77d0f4e1a2b3c4d5e6f70718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c",
        RecordHash = "c44e1f2a3b4c5d6e7f8091a2b3c4d5e6f70819293a4b5c6d7e8f90a1b2c3d4e5",
        Signature = "6d4509712e1f3a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3",
        SigningKeyId = "audit-hmac/v1",
    };

    static AgentInvocation Invocation() => new()
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
    };
}
