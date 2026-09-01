using Frontier.Platform.Abstractions;
using Xunit;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

public sealed class AuditProjectionTests
{
    private static readonly IReadOnlyList<ToolCall> EmptyToolCalls = Array.Empty<ToolCall>();

    #region HumanDecisionRecord Tests

    [Fact]
    public void HumanDecisionRecord_WithAllProperties_IsConstructible()
    {
        var timestamp = System.DateTime.UtcNow;
        var record = new HumanDecisionRecord
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-1",
            Kind = DecisionKind.Approve,
            Notes = "Approved",
            DecidedAtUtc = timestamp
        };

        Assert.Equal("gate-1", record.GateId);
        Assert.Equal("approver-1", record.ApproverId);
        Assert.Equal(DecisionKind.Approve, record.Kind);
    }

    [Fact]
    public void HumanDecisionRecord_WithNullNotes_IsConstructible()
    {
        var record = new HumanDecisionRecord
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-1",
            Kind = DecisionKind.Reject,
            Notes = null,
            DecidedAtUtc = System.DateTime.UtcNow
        };

        Assert.Null(record.Notes);
    }

    [Fact]
    public void HumanDecisionRecord_Equality_ConsidersAllProperties()
    {
        var timestamp = System.DateTime.UtcNow;
        var record1 = new HumanDecisionRecord
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-1",
            Kind = DecisionKind.Approve,
            Notes = "OK",
            DecidedAtUtc = timestamp
        };

        var record2 = new HumanDecisionRecord
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-1",
            Kind = DecisionKind.Approve,
            Notes = "OK",
            DecidedAtUtc = timestamp
        };

        Assert.Equal(record1, record2);
    }

    #endregion

    #region AuditTelemetryRecord Tests

    [Fact]
    public void AuditTelemetryRecord_WithAllProperties_IsConstructible()
    {
        var resolved = CreateResolvedModelSummary();
        var record = new AuditTelemetryRecord
        {
            ExecutionId = "exec-1",
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            ArtifactKey = "scope",
            AgentRole = "analyst",
            ResolvedModel = resolved,
            InputContractType = "Input",
            InputHash = "hash1",
            OutputContractType = "Output",
            OutputHash = "hash2",
            InputTokens = 1000,
            OutputTokens = 500,
            CacheReadTokens = 0,
            CacheWriteTokens = 50,
            RetryCount = 0,
            LatencyMs = 2500,
            ToolCalls = EmptyToolCalls,
            BaselineCacheChanged = false,
            DynamicCacheChanged = false,
            RealTimeCacheChanged = false,
            InvokedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal("exec-1", record.ExecutionId);
        Assert.Equal("analyst", record.AgentRole);
        Assert.Equal(1000, record.InputTokens);
    }

    [Fact]
    public void AuditTelemetryRecord_Equality_ConsidersAllProperties()
    {
        var resolved = CreateResolvedModelSummary();
        var timestamp = System.DateTime.UtcNow;

        var record1 = new AuditTelemetryRecord
        {
            ExecutionId = "exec-1",
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            AgentRole = "analyst",
            ResolvedModel = resolved,
            InputContractType = "Input",
            InputHash = "hash1",
            OutputContractType = "Output",
            OutputHash = "hash2",
            InputTokens = 100,
            OutputTokens = 50,
            CacheReadTokens = 0,
            CacheWriteTokens = 0,
            RetryCount = 0,
            LatencyMs = 1000,
            ToolCalls = EmptyToolCalls,
            BaselineCacheChanged = false,
            DynamicCacheChanged = false,
            RealTimeCacheChanged = false,
            InvokedAtUtc = timestamp
        };

        var record2 = new AuditTelemetryRecord
        {
            ExecutionId = "exec-1",
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            AgentRole = "analyst",
            ResolvedModel = resolved,
            InputContractType = "Input",
            InputHash = "hash1",
            OutputContractType = "Output",
            OutputHash = "hash2",
            InputTokens = 100,
            OutputTokens = 50,
            CacheReadTokens = 0,
            CacheWriteTokens = 0,
            RetryCount = 0,
            LatencyMs = 1000,
            ToolCalls = EmptyToolCalls,
            BaselineCacheChanged = false,
            DynamicCacheChanged = false,
            RealTimeCacheChanged = false,
            InvokedAtUtc = timestamp
        };

        Assert.Equal(record1, record2);
    }

    #endregion

    #region ConsolidateAuditInput Tests

    [Fact]
    public void ConsolidateAuditInput_WithAllProperties_IsConstructible()
    {
        var timestamp = System.DateTime.UtcNow;
        var input = new ConsolidateAuditInput
        {
            ExecutionId = "exec-1",
            EngagementId = "eng-1",
            WorkflowId = "wf-1",
            DefinitionHash = "sha256:hash123",
            StartedAtUtc = timestamp
        };

        Assert.Equal("exec-1", input.ExecutionId);
        Assert.Equal("sha256:hash123", input.DefinitionHash);
    }

    [Fact]
    public void ConsolidateAuditInput_Equality_ConsidersAllProperties()
    {
        var timestamp = System.DateTime.UtcNow;

        var input1 = new ConsolidateAuditInput
        {
            ExecutionId = "exec-1",
            EngagementId = "eng-1",
            WorkflowId = "wf-1",
            DefinitionHash = "sha256:abc",
            StartedAtUtc = timestamp
        };

        var input2 = new ConsolidateAuditInput
        {
            ExecutionId = "exec-1",
            EngagementId = "eng-1",
            WorkflowId = "wf-1",
            DefinitionHash = "sha256:abc",
            StartedAtUtc = timestamp
        };

        Assert.Equal(input1, input2);
    }

    #endregion

    #region Helpers

    private static ResolvedModelSummary CreateResolvedModelSummary()
    {
        return new ResolvedModelSummary
        {
            RoleId = "analyst",
            Provider = "anthropic",
            ModelId = "claude-fable-5",
            ModelVersion = "1.0",
            ChainPosition = 0,
            MappingVersion = 1
        };
    }

    #endregion
}
