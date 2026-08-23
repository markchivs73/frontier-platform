using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Minimal <see cref="SignedAuditRecord"/> fixture for <see cref="GraphOrchestratorTests"/>'s
/// <see cref="WorkflowActivityNames.ConsolidateAuditActivity"/> handler (S5.6): the
/// orchestrator only needs a well-formed response, not a realistic one — consolidation and
/// signing are exercised by <c>Frontier.Platform.Audit.Tests</c>.
/// </summary>
internal static class AuditFixtures
{
    /// <summary>Builds an <see cref="AuditRecord"/> for <paramref name="input"/>'s execution with empty collections and zeroed cache metrics.</summary>
    public static AuditRecord UnsignedRecord(ConsolidateAuditInput input)
    {
        var (engagementId, workflowId) = SplitExecutionId(input.ExecutionId);

        return new AuditRecord
        {
            ExecutionId = input.ExecutionId,
            EngagementId = engagementId,
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            DefinitionHash = input.DefinitionHash,
            StartedAtUtc = input.StartedAtUtc,
            ClosedAtUtc = input.StartedAtUtc,
            FinalStatus = ExecutionStatus.Completed,
            OrchestrationEvents = [],
            AgentInvocations = [],
            ValidatorOutcomes = [],
            HumanDecisions = [],
            CacheMetrics = EmptyCacheMetrics(),
        };
    }

    /// <summary>Builds a <see cref="SignedAuditRecord"/> for <paramref name="input"/>'s execution with empty collections and zeroed cache metrics.</summary>
    public static SignedAuditRecord SignedRecord(ConsolidateAuditInput input) => SignedRecord(UnsignedRecord(input));

    /// <summary>Chains and signs <paramref name="record"/> with fixed fixture values (S5.6: <see cref="ConsolidateAuditActivity"/> only needs a well-formed response).</summary>
    public static SignedAuditRecord SignedRecord(AuditRecord record) => new()
    {
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
        PreviousRecordHash = "genesis-hash",
        RecordHash = "record-hash",
        Signature = "signature",
        SigningKeyId = "dev-key/v1",
    };

    /// <summary>Zeroed <see cref="CacheMetrics"/> across all three tiers.</summary>
    internal static CacheMetrics EmptyCacheMetrics()
    {
        var emptyTier = new CacheTierMetrics { Reads = 0, Writes = 0, HitRatePercent = 0m, TokensRead = 0 };
        return new CacheMetrics { Baseline = emptyTier, Dynamic = emptyTier, RealTime = emptyTier };
    }

    /// <summary>Splits a <c>{engagementId}::{workflowId}</c> instance id (rule 3) into its parts.</summary>
    internal static (string EngagementId, string WorkflowId) SplitExecutionId(string executionId)
    {
        var parts = executionId.Split("::", 2);
        return (parts[0], parts[1]);
    }
}
