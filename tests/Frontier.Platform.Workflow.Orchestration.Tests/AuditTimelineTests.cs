using Frontier.Platform.Abstractions;
using Xunit;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

public sealed class AuditTimelineTests
{
    private static readonly IReadOnlyList<ToolCall> EmptyToolCalls = Array.Empty<ToolCall>();

    #region AgentInvocation Tests

    [Fact]
    public void AgentInvocation_WithAllProperties_IsConstructible()
    {
        var invocation = new AgentInvocation
        {
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            ArtifactKey = "scope",
            AgentRole = "analyst",
            ResolvedModel = CreateResolvedModelSummary(),
            InputContractType = "BriefArtifact",
            InputHash = "sha256:abc123",
            OutputContractType = "SummaryArtifact",
            OutputHash = "sha256:def456",
            InputTokens = 1000,
            OutputTokens = 500,
            CacheReadTokens = 0,
            CacheWriteTokens = 50,
            RetryCount = 0,
            LatencyMs = 2500,
            ToolCalls = EmptyToolCalls,
            InvokedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal("corr-1", invocation.CorrelationId);
        Assert.Equal("agent-1", invocation.NodeId);
        Assert.Equal("scope", invocation.ArtifactKey);
        Assert.Equal("analyst", invocation.AgentRole);
        Assert.Equal(1000, invocation.InputTokens);
        Assert.Equal(500, invocation.OutputTokens);
    }

    [Fact]
    public void AgentInvocation_WithTokenMetrics_IsConstructible()
    {
        var invocation = new AgentInvocation
        {
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            AgentRole = "analyst",
            ResolvedModel = CreateResolvedModelSummary(),
            InputContractType = "Input",
            InputHash = "hash1",
            OutputContractType = "Output",
            OutputHash = "hash2",
            InputTokens = 5000,
            OutputTokens = 2000,
            CacheReadTokens = 1500,
            CacheWriteTokens = 300,
            RetryCount = 1,
            LatencyMs = 5000,
            ToolCalls = EmptyToolCalls,
            InvokedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal(5000, invocation.InputTokens);
        Assert.Equal(1500, invocation.CacheReadTokens);
        Assert.Equal(1, invocation.RetryCount);
    }

    [Fact]
    public void AgentInvocation_WithoutArtifactKey_IsConstructible()
    {
        var invocation = new AgentInvocation
        {
            CorrelationId = "corr-1",
            NodeId = "agent-1",
            AgentRole = "analyst",
            ResolvedModel = CreateResolvedModelSummary(),
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
            InvokedAtUtc = System.DateTime.UtcNow
        };

        Assert.Null(invocation.ArtifactKey);
    }

    [Fact]
    public void AgentInvocation_Equality_ConsidersAllProperties()
    {
        var resolved = CreateResolvedModelSummary();
        var timestamp = System.DateTime.UtcNow;

        var inv1 = new AgentInvocation
        {
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
            InvokedAtUtc = timestamp
        };

        var inv2 = new AgentInvocation
        {
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
            InvokedAtUtc = timestamp
        };

        Assert.Equal(inv1, inv2);
    }

    #endregion

    #region HitlDecision Tests

    [Fact]
    public void HitlDecision_WithApprovalDecision_IsConstructible()
    {
        var decision = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-123",
            Kind = DecisionKind.Approve,
            Notes = "Looks good",
            RollbackToNodeId = null,
            DecidedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal("gate-1", decision.GateId);
        Assert.Equal("approver-123", decision.ApproverId);
        Assert.Equal(DecisionKind.Approve, decision.Kind);
    }

    [Fact]
    public void HitlDecision_WithRejectionAndRollback_IsConstructible()
    {
        var decision = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-123",
            Kind = DecisionKind.Reject,
            Notes = "Needs revision",
            RollbackToNodeId = "agent-previous",
            DecidedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal(DecisionKind.Reject, decision.Kind);
        Assert.Equal("agent-previous", decision.RollbackToNodeId);
    }

    [Fact]
    public void HitlDecision_WithEscalation_IsConstructible()
    {
        var decision = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "escalated-to",
            Kind = DecisionKind.Escalate,
            Notes = "Escalating to leadership",
            RollbackToNodeId = null,
            DecidedAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal(DecisionKind.Escalate, decision.Kind);
    }

    [Fact]
    public void HitlDecision_Equality_ConsidersAllProperties()
    {
        var timestamp = System.DateTime.UtcNow;

        var decision1 = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-123",
            Kind = DecisionKind.Approve,
            Notes = "Approved",
            RollbackToNodeId = null,
            DecidedAtUtc = timestamp
        };

        var decision2 = new HitlDecision
        {
            GateId = "gate-1",
            RequestId = "req-1",
            ApproverId = "approver-123",
            Kind = DecisionKind.Approve,
            Notes = "Approved",
            RollbackToNodeId = null,
            DecidedAtUtc = timestamp
        };

        Assert.Equal(decision1, decision2);
    }

    #endregion

    #region WorkflowEvent Tests

    [Fact]
    public void WorkflowEvent_TaskScheduled_IsConstructible()
    {
        var evt = new WorkflowEvent
        {
            CorrelationId = "corr-1",
            EventType = WorkflowEventType.TaskScheduled,
            NodeId = "agent-1",
            OccurredAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal("corr-1", evt.CorrelationId);
        Assert.Equal(WorkflowEventType.TaskScheduled, evt.EventType);
    }

    [Fact]
    public void WorkflowEvent_TaskCompleted_IsConstructible()
    {
        var evt = new WorkflowEvent
        {
            CorrelationId = "corr-1",
            EventType = WorkflowEventType.TaskCompleted,
            NodeId = "agent-1",
            OccurredAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal(WorkflowEventType.TaskCompleted, evt.EventType);
    }

    [Fact]
    public void WorkflowEvent_WithDetails_IsConstructible()
    {
        var evt = new WorkflowEvent
        {
            CorrelationId = "corr-1",
            EventType = WorkflowEventType.TaskFailed,
            NodeId = "agent-1",
            Details = "Connection timeout",
            OccurredAtUtc = System.DateTime.UtcNow
        };

        Assert.Equal("Connection timeout", evt.Details);
    }

    [Fact]
    public void WorkflowEvent_Equality_ConsidersAllProperties()
    {
        var timestamp = System.DateTime.UtcNow;

        var evt1 = new WorkflowEvent
        {
            CorrelationId = "corr-1",
            EventType = WorkflowEventType.TaskScheduled,
            NodeId = "agent-1",
            OccurredAtUtc = timestamp
        };

        var evt2 = new WorkflowEvent
        {
            CorrelationId = "corr-1",
            EventType = WorkflowEventType.TaskScheduled,
            NodeId = "agent-1",
            OccurredAtUtc = timestamp
        };

        Assert.Equal(evt1, evt2);
    }

    #endregion

    #region StepCompletion Tests

    [Fact]
    public void StepCompletion_WithAllProperties_IsConstructible()
    {
        var completion = new StepCompletion
        {
            NodeId = "agent-1",
            NodeType = NodeType.AgentTask,
            ArtifactKey = "scope",
            CorrelationId = "corr-1",
            OutputContractType = "SummaryArtifact",
            OutputHash = "sha256:abc123",
            RetryCount = 0,
            CompletedAtUtc = System.DateTime.UtcNow,
            ResolvedModel = CreateResolvedModelSummary()
        };

        Assert.Equal("agent-1", completion.NodeId);
        Assert.Equal(NodeType.AgentTask, completion.NodeType);
        Assert.Equal("scope", completion.ArtifactKey);
    }

    [Fact]
    public void StepCompletion_WithDifferentNodeTypes_IsConstructible()
    {
        var gateCompletion = new StepCompletion
        {
            NodeId = "gate-1",
            NodeType = NodeType.HumanGate,
            ArtifactKey = "approach",
            CorrelationId = "corr-1",
            OutputContractType = "PlanArtifact",
            OutputHash = "hash",
            RetryCount = 0,
            CompletedAtUtc = System.DateTime.UtcNow,
            ResolvedModel = CreateResolvedModelSummary()
        };

        Assert.Equal(NodeType.HumanGate, gateCompletion.NodeType);
    }

    [Fact]
    public void StepCompletion_Equality_ConsidersAllProperties()
    {
        var resolved = CreateResolvedModelSummary();
        var timestamp = System.DateTime.UtcNow;

        var comp1 = new StepCompletion
        {
            NodeId = "agent-1",
            NodeType = NodeType.AgentTask,
            ArtifactKey = "scope",
            CorrelationId = "corr-1",
            OutputContractType = "SummaryArtifact",
            OutputHash = "hash",
            RetryCount = 0,
            CompletedAtUtc = timestamp,
            ResolvedModel = resolved
        };

        var comp2 = new StepCompletion
        {
            NodeId = "agent-1",
            NodeType = NodeType.AgentTask,
            ArtifactKey = "scope",
            CorrelationId = "corr-1",
            OutputContractType = "SummaryArtifact",
            OutputHash = "hash",
            RetryCount = 0,
            CompletedAtUtc = timestamp,
            ResolvedModel = resolved
        };

        Assert.Equal(comp1, comp2);
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
