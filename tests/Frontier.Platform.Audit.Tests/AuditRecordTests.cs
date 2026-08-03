using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

public sealed class AuditRecordTests
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

    static AuditRecord Record() => new()
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
