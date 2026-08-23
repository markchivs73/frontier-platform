using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Audit.Tests;

/// <summary>
/// Sample builders for the audit contract family (S11.5, ADR-PA2): moved with their types
/// from the subsystem's <c>ContractSamples</c> when the contracts entered Platform.Audit.
/// Values are byte-identical to the originals — the golden files did not change.
/// </summary>
internal static class AuditContractSamples
{
    /// <summary>A well-formed <see cref="ResolvedModelSummary"/> (kernel type; local copy of the subsystem sample).</summary>
    public static ResolvedModelSummary ResolvedModelSummary() => new()
    {
        RoleId = "deep-reasoning",
        Provider = "anthropic",
        ModelId = "claude-fable-5",
        ModelVersion = "2026-05-01",
        ChainPosition = 0,
        MappingVersion = 1,
    };

    /// <summary>A well-formed <see cref="ToolCall"/>; always <c>[]</c> on real records until Stage 6.</summary>
    public static ToolCall ToolCall() => new()
    {
        Name = "connectors/crm.create_opportunity",
        InvokedAtUtc = new DateTime(2026, 1, 1, 0, 22, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="WorkflowEvent"/>.</summary>
    public static WorkflowEvent WorkflowEvent() => new()
    {
        EventType = WorkflowEventType.TaskCompleted,
        NodeId = "pricing-agent",
        CorrelationId = "corr-3",
        OccurredAtUtc = new DateTime(2026, 1, 1, 0, 25, 0, DateTimeKind.Utc),
        Details = "Pricing section produced.",
    };

    /// <summary>A well-formed <see cref="ValidatorOutcome"/>; <see cref="SignedAuditRecord.ValidatorOutcomes"/> is <c>[]</c> until Stage 6.</summary>
    public static ValidatorOutcome ValidatorOutcome() => new()
    {
        CorrelationId = "corr-3",
        ValidatorId = "pricing-qa",
        TargetArtifactKey = "pricing",
        Status = ValidatorStatus.Pass,
        FindingCodes = [],
        RanAtUtc = new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="HumanDecisionRecord"/>.</summary>
    public static HumanDecisionRecord HumanDecisionRecord() => new()
    {
        GateId = "human-gate",
        RequestId = "eng-1::wf-1:human-gate:1",
        ApproverId = "approver-1",
        Kind = DecisionKind.Approve,
        Notes = "Looks good.",
        DecidedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="AgentInvocation"/>.</summary>
    public static AgentInvocation AgentInvocation() => new()
    {
        CorrelationId = "corr-3",
        NodeId = "pricing-agent",
        ArtifactKey = "pricing",
        AgentRole = "deep-reasoning",
        ResolvedModel = ResolvedModelSummary(),
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
        ToolCalls = [ToolCall()],
        InvokedAtUtc = new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="CacheMetrics"/> (doc 05 §6).</summary>
    public static CacheMetrics CacheMetrics() => new()
    {
        Baseline = new CacheTierMetrics { Reads = 14, Writes = 1, HitRatePercent = 93.3m, TokensRead = 182000 },
        Dynamic = new CacheTierMetrics { Reads = 11, Writes = 2, HitRatePercent = 78.6m, TokensRead = 0 },
        RealTime = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 },
    };

    /// <summary>A well-formed <see cref="AuditTelemetryRecord"/> (C-14 staging shape).</summary>
    public static AuditTelemetryRecord AuditTelemetryRecord() => new()
    {
        ExecutionId = "eng-1::wf-1",
        CorrelationId = "corr-3",
        NodeId = "pricing-agent",
        ArtifactKey = "pricing",
        AgentRole = "deep-reasoning",
        ResolvedModel = ResolvedModelSummary(),
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
        ToolCalls = [ToolCall()],
        BaselineCacheChanged = false,
        DynamicCacheChanged = true,
        RealTimeCacheChanged = false,
        InvokedAtUtc = new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="AuditQuery"/> exercising every filter dimension.</summary>
    public static AuditQuery AuditQuery() => new()
    {
        EngagementId = "eng-1",
        ModelId = "claude-fable-5",
        ValidatorId = "pricing-qa",
        OverridesOnly = false,
        FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ToUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
    };

    /// <summary>A well-formed <see cref="AuditSummary"/>.</summary>
    public static AuditSummary AuditSummary() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        FinalStatus = ExecutionStatus.Completed,
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="VerificationResult"/> for a clean chain.</summary>
    public static VerificationResult VerificationResult() => new()
    {
        SignatureValid = true,
        ChainValid = true,
        BrokenLinkAt = null,
        VerifiedAgainstKeyId = "audit-hmac/v1",
    };

    /// <summary>A well-formed <see cref="AuditRecord"/> (doc 05 §3 fields 0-13) covering one Gate-3-style execution.</summary>
    public static AuditRecord AuditRecord() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        FinalStatus = ExecutionStatus.Completed,
        OrchestrationEvents = [WorkflowEvent()],
        AgentInvocations = [AgentInvocation()],
        ValidatorOutcomes = [],
        HumanDecisions = [HumanDecisionRecord()],
        CacheMetrics = CacheMetrics(),
    };

    /// <summary>A well-formed <see cref="SignedAuditRecord"/> (doc 05 §3, all 18 fields).</summary>
    public static SignedAuditRecord SignedAuditRecord() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ClosedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        FinalStatus = ExecutionStatus.Completed,
        OrchestrationEvents = [WorkflowEvent()],
        AgentInvocations = [AgentInvocation()],
        ValidatorOutcomes = [],
        HumanDecisions = [HumanDecisionRecord()],
        CacheMetrics = CacheMetrics(),
        PreviousRecordHash = "77d0f4e1a2b3c4d5e6f70718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c",
        RecordHash = "c44e1f2a3b4c5d6e7f8091a2b3c4d5e6f70819293a4b5c6d7e8f90a1b2c3d4e5",
        Signature = "6d4509712e1f3a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3",
        SigningKeyId = "audit-hmac/v1",
    };
}
