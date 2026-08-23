using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Orchestration;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S5.4 tests for <see cref="WorkflowEventProjector"/> (doc 05 §4 step 1, QG-5 verdict).</summary>
public sealed class WorkflowEventProjectorTests
{
    private static readonly DateTime StartedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Project_OrdersStepsAndDecisionsBetweenLifecycleBookends()
    {
        var events = WorkflowEventProjector.Project(Snapshot(), StartedAtUtc);

        Assert.Equal(4, events.Count);
        Assert.Equal(WorkflowEventType.ExecutionStarted, events[0].EventType);
        Assert.Equal(WorkflowEventType.TaskCompleted, events[1].EventType);
        Assert.Equal(WorkflowEventType.ExternalEventRaised, events[2].EventType);
        Assert.Equal(WorkflowEventType.ExecutionCompleted, events[3].EventType);
    }

    [Fact]
    public void Project_StartBookend_UsesStartedAtUtcWithNoNodeOrCorrelationId()
    {
        var events = WorkflowEventProjector.Project(Snapshot(), StartedAtUtc);

        var started = events[0];
        Assert.Equal(StartedAtUtc, started.OccurredAtUtc);
        Assert.Null(started.NodeId);
        Assert.Null(started.CorrelationId);
        Assert.Null(started.Details);
    }

    [Fact]
    public void Project_CompletedBookend_UsesCheckpointTimestampAndFinalStatus()
    {
        var snapshot = Snapshot();
        var events = WorkflowEventProjector.Project(snapshot, StartedAtUtc);

        var completed = events[3];
        Assert.Equal(snapshot.CheckpointedAtUtc, completed.OccurredAtUtc);
        Assert.Equal("completed", completed.Details);
    }

    [Fact]
    public void ToTaskCompleted_MapsStepFieldsToWorkflowEvent()
    {
        var step = Snapshot().CompletedSteps[0];

        var workflowEvent = WorkflowEventProjector.ToTaskCompleted(step);

        Assert.Equal(WorkflowEventType.TaskCompleted, workflowEvent.EventType);
        Assert.Equal(step.NodeId, workflowEvent.NodeId);
        Assert.Equal(step.CorrelationId, workflowEvent.CorrelationId);
        Assert.Equal(step.CompletedAtUtc, workflowEvent.OccurredAtUtc);
        Assert.Equal(step.OutputContractType, workflowEvent.Details);
    }

    [Fact]
    public void ToExternalEventRaised_MapsDecisionFieldsToWorkflowEvent()
    {
        var decision = Snapshot().Decisions[0];

        var workflowEvent = WorkflowEventProjector.ToExternalEventRaised(decision);

        Assert.Equal(WorkflowEventType.ExternalEventRaised, workflowEvent.EventType);
        Assert.Equal(decision.GateId, workflowEvent.NodeId);
        Assert.Equal(decision.RequestId, workflowEvent.CorrelationId);
        Assert.Equal(decision.DecidedAtUtc, workflowEvent.OccurredAtUtc);
        Assert.Equal("approve", workflowEvent.Details);
    }

    [Fact]
    public void Bookend_BuildsEventWithNoNodeOrCorrelationId()
    {
        var occurredAtUtc = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc);

        var workflowEvent = WorkflowEventProjector.Bookend(WorkflowEventType.ExecutionStarted, occurredAtUtc, details: null);

        Assert.Equal(WorkflowEventType.ExecutionStarted, workflowEvent.EventType);
        Assert.Null(workflowEvent.NodeId);
        Assert.Null(workflowEvent.CorrelationId);
        Assert.Equal(occurredAtUtc, workflowEvent.OccurredAtUtc);
        Assert.Null(workflowEvent.Details);
    }

    /// <summary>A completed execution with one completed step and one human decision (mirrors doc 05's Gate-3-style execution).</summary>
    internal static ExecutionSnapshot Snapshot() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 2,
        Status = ExecutionStatus.Completed,
        Artifacts = new Dictionary<string, ArtifactStatus> { ["scope"] = ArtifactStatus.Approved },
        CompletedSteps =
        [
            new StepCompletion
            {
                NodeId = "scope-agent",
                NodeType = NodeType.AgentTask,
                ArtifactKey = "scope",
                CorrelationId = "corr-1",
                OutputContractType = "SummaryArtifact",
                OutputHash = "abc123",
                RetryCount = 0,
                CompletedAtUtc = new DateTime(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc),
            },
        ],
        Decisions =
        [
            new HitlDecision
            {
                GateId = "human-gate",
                RequestId = "eng-1::wf-1:human-gate:1",
                ApproverId = "approver-1",
                Kind = DecisionKind.Approve,
                DecidedAtUtc = new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc),
            },
        ],
        ApprovedSnapshotRefs = new Dictionary<string, string>(),
        CheckpointedAtUtc = new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc),
    };
}
