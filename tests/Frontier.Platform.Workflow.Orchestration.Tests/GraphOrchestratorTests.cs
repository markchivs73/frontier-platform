using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S2.2 tests for the <see cref="GraphOrchestrator"/> entry point.</summary>
public sealed class GraphOrchestratorTests
{
    private readonly GraphOrchestrator orchestrator = new(new FakeResiliencePolicyProvider(), new RollbackPlanner(), OrchestrationFixtures.WriteClassifier);

    [Fact]
    public async Task RunAsync_NullContext_Throws()
    {
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.RunAsync(null!, input));
    }

    [Fact]
    public async Task RunAsync_NullInput_Throws()
    {
        var context = new FakeTaskOrchestrationContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.RunAsync(context, null!));
    }

    [Fact]
    public async Task RunAsync_ThreeArtifactChainWithNoCascade_RunsInitialWalkOnly()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = activityInput =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)activityInput!).GetAwaiter().GetResult();
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = activityInput =>
            new SnapshotActivityResponse { SnapshotId = $"{((ExecutionSnapshot)activityInput!).ExecutionId}:{((ExecutionSnapshot)activityInput!).Sequence:D6}" };
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = activityInput =>
            new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)activityInput!).ExecutionId}:{((ArtifactStateActivityRequest)activityInput!).ArtifactKey}:v{((ArtifactStateActivityRequest)activityInput!).Version}" };
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
            AuditFixtures.SignedRecord((ConsolidateAuditInput)activityInput!);
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        var result = await orchestrator.RunAsync(context, input);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], result.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Draft, result.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Draft, result.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Draft, result.ArtifactStatuses["pricing"]);
    }

    [Fact]
    public async Task RunAsync_ThreeArtifactChainWithNoCascade_CallsConsolidateAuditActivityExactlyOnceAfterFinalSnapshot()
    {
        var context = new FakeTaskOrchestrationContext();
        var callOrder = new List<string>();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = activityInput =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)activityInput!).GetAwaiter().GetResult();
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = activityInput =>
        {
            var snapshot = (ExecutionSnapshot)activityInput!;
            callOrder.Add($"{WorkflowActivityNames.SnapshotStateActivity}:{snapshot.Status}");
            return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
        };
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = activityInput =>
            new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)activityInput!).ExecutionId}:{((ArtifactStateActivityRequest)activityInput!).ArtifactKey}:v{((ArtifactStateActivityRequest)activityInput!).Version}" };
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
        {
            callOrder.Add(WorkflowActivityNames.ConsolidateAuditActivity);
            return AuditFixtures.SignedRecord((ConsolidateAuditInput)activityInput!);
        };
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        await orchestrator.RunAsync(context, input);

        Assert.Equal(1, callOrder.Count(call => call == WorkflowActivityNames.ConsolidateAuditActivity));
        Assert.Equal(WorkflowActivityNames.ConsolidateAuditActivity, callOrder[^1]);
        Assert.Equal($"{WorkflowActivityNames.SnapshotStateActivity}:{ExecutionStatus.Completed}", callOrder[^2]);
    }

    [Fact]
    public async Task RunAsync_ArtifactUpdatedEvent_AlsoRunsCascadeRegeneration()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = activityInput =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)activityInput!).GetAwaiter().GetResult();
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = activityInput =>
            new SnapshotActivityResponse { SnapshotId = $"{((ExecutionSnapshot)activityInput!).ExecutionId}:{((ExecutionSnapshot)activityInput!).Sequence:D6}" };
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = activityInput =>
            new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)activityInput!).ExecutionId}:{((ArtifactStateActivityRequest)activityInput!).ArtifactKey}:v{((ArtifactStateActivityRequest)activityInput!).Version}" };
        context.ExternalEvents[GraphOrchestratorSteps.ArtifactUpdatedEventName] = "scope";
        context.ActivityHandlers[WorkflowActivityNames.EvaluateCascadeActivity] = _ => new CascadeActivityResponse
        {
            ChangedArtifact = "scope",
            DownstreamArtifacts = ["approach", "pricing"],
            SkippedArtifacts = [],
        };
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
            AuditFixtures.SignedRecord((ConsolidateAuditInput)activityInput!);
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ThreeArtifactChain(), EngagementId = "eng-1" };

        var result = await orchestrator.RunAsync(context, input);

        Assert.Equal(
            ["scope-agent", "approach-agent", "pricing-agent", "approach-agent", "pricing-agent"],
            result.CompletedSteps.Select(step => step.NodeId));
    }

    [Fact]
    public async Task RunAsync_ChainWithBusinessGateApproved_CompletesWithApprovedArtifacts()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = activityInput =>
            new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)activityInput!).GetAwaiter().GetResult();
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = activityInput =>
            new SnapshotActivityResponse { SnapshotId = $"{((ExecutionSnapshot)activityInput!).ExecutionId}:{((ExecutionSnapshot)activityInput!).Sequence:D6}" };
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = activityInput =>
            new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)activityInput!).ExecutionId}:{((ArtifactStateActivityRequest)activityInput!).ArtifactKey}:v{((ArtifactStateActivityRequest)activityInput!).Version}" };
        context.ActivityHandlers[WorkflowActivityNames.RequestApprovalActivity] = activityInput =>
        {
            var request = (GateOpenRequest)activityInput!;
            return new ApprovalRequest
            {
                Id = $"{request.ExecutionId}:{request.GateId}:{request.Occurrence}",
                EngagementId = request.EngagementId,
                ExecutionId = request.ExecutionId,
                GateId = request.GateId,
                GateKind = request.GateKind,
                ApproverRoles = request.ApproverRoles,
                SectionRefs = request.SectionRefs,
                Status = ApprovalRequestStatus.Pending,
                RequestedAtUtc = request.RequestedAtUtc,
            };
        };
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-business-1")] = new HitlDecision
        {
            GateId = "gate-business-1",
            RequestId = "eng-1::wf-chain-gate:gate-business-1:0",
            ApproverId = "user:approver-1",
            Kind = DecisionKind.Approve,
            DecidedAtUtc = context.CurrentUtcDateTime,
        };
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
            AuditFixtures.SignedRecord((ConsolidateAuditInput)activityInput!);
        var input = new GraphOrchestratorInput { Definition = OrchestrationFixtures.ChainWithBusinessGate(), EngagementId = "eng-1" };

        var result = await orchestrator.RunAsync(context, input);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], result.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Approved, result.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Approved, result.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Approved, result.ArtifactStatuses["pricing"]);
    }
}
