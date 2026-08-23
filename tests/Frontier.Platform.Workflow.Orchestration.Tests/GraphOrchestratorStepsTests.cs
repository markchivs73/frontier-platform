using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S2.2 tests for the step logic in <see cref="GraphOrchestratorSteps"/>.</summary>
public sealed class GraphOrchestratorStepsTests
{
    private static readonly FakeResiliencePolicyProvider PolicyProvider = new();

    [Fact]
    public void EnsureSupported_OneShotAllAgentTasks_DoesNotThrow()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();

        GraphOrchestratorSteps.EnsureSupported(definition);
    }

    [Fact]
    public void EnsureSupported_DispatcherMode_ThrowsContractViolationException()
    {
        var definition = OrchestrationFixtures.DispatcherModeChain();

        var exception = Assert.Throws<ContractViolationException>(() => GraphOrchestratorSteps.EnsureSupported(definition));

        Assert.Contains(ExecutionMode.OneShot.Name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSupported_UnsupportedNodeType_ThrowsContractViolationException()
    {
        var definition = OrchestrationFixtures.WithUnsupportedNode();

        var exception = Assert.Throws<ContractViolationException>(() => GraphOrchestratorSteps.EnsureSupported(definition));

        Assert.Contains("branch-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureSupported_ChainWithBusinessGate_DoesNotThrow()
    {
        var definition = OrchestrationFixtures.ChainWithBusinessGate();

        GraphOrchestratorSteps.EnsureSupported(definition);
    }

    [Fact]
    public void BuildActivityInput_MapsNodeFieldsAndCorrelationId()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition);
        var node = (AgentTaskNode)definition.Nodes[0];
        var state = new GraphExecutionState();

        var activityInput = GraphOrchestratorSteps.BuildActivityInput(input, node, "correlation-1", "eng-1::wf-chain", state);

        Assert.Equal(node.NodeId, activityInput.NodeId);
        Assert.Equal(node.ArtifactKey, activityInput.ArtifactKey);
        Assert.Equal(node.Role, activityInput.Role);
        Assert.Equal(node.InstructionsRef, activityInput.InstructionsRef);
        Assert.Equal(node.InputContractType, activityInput.InputContractType);
        Assert.Equal(node.OutputContractType, activityInput.OutputContractType);
        Assert.Equal("correlation-1", activityInput.CorrelationId);
        Assert.Equal(input.EngagementId, activityInput.EngagementId);
        Assert.Equal("eng-1::wf-chain", activityInput.ExecutionId);
        // S9.28: no longer the same instance — BuildActivityInput substitutes EngagementId
        // (see BuildActivityInput_SubstitutesRealEngagementIdIntoContextRequest below); every
        // other field carries through unchanged. Here node/input share "eng-1" already, so
        // value equality still holds.
        Assert.Equal(node.ContextRequest with { EngagementId = activityInput.ContextRequest.EngagementId }, activityInput.ContextRequest);
        Assert.Null(activityInput.UpstreamPayload);
    }

    [Fact]
    public void BuildActivityInput_SubstitutesRealEngagementIdIntoContextRequest()
    {
        // S9.28: a published definition runs across many engagements (ADR-2) — the node's
        // authored ContextRequest.EngagementId (here the fixture's hardcoded "eng-1") can
        // never be the real running engagement at design time, so BuildActivityInput must
        // override it with the orchestration's actual engagement id ("eng-999" here). Never
        // caught before S9.28 because every gate/demo fixture's authored value happened to
        // already match the one seeded engagement it ever ran against.
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition, engagementId: "eng-999");
        var node = (AgentTaskNode)definition.Nodes[0];
        var state = new GraphExecutionState();

        var activityInput = GraphOrchestratorSteps.BuildActivityInput(input, node, "correlation-1", "eng-999::wf-chain", state);

        Assert.Equal("eng-1", node.ContextRequest.EngagementId);
        Assert.Equal("eng-999", activityInput.ContextRequest.EngagementId);
    }

    [Fact]
    public void BuildActivityInput_NodeWithDataEdgePredecessor_ResolvesUpstreamPayloadFromState()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition);
        var node = (AgentTaskNode)definition.Nodes[1];
        var state = new GraphExecutionState();
        state.NodeOutputPayloads["scope-agent"] = "scope-payload";

        var activityInput = GraphOrchestratorSteps.BuildActivityInput(input, node, "correlation-2", "eng-1::wf-chain", state);

        Assert.Equal("scope-payload", activityInput.UpstreamPayload);
    }

    [Fact]
    public void BuildStepCompletion_MapsResultFieldsAndCompletionTimestamp()
    {
        var context = new FakeTaskOrchestrationContext();
        var node = (AgentTaskNode)OrchestrationFixtures.ThreeArtifactChain().Nodes[0];
        var result = new AgentTaskActivityResult
        {
            NodeId = node.NodeId,
            ArtifactKey = node.ArtifactKey,
            OutputContractType = node.OutputContractType,
            OutputPayload = "stub-output:scope-agent:correlation-1",
            OutputHash = "deadbeef",
            ResolvedModel = new ResolvedModelSummary
            {
                RoleId = "deep-reasoning",
                Provider = "anthropic",
                ModelId = "claude-fable-5",
                ChainPosition = 0,
                MappingVersion = 1,
            },
            HostBuild = "1.2.3+abc1234",
        };

        var completion = GraphOrchestratorSteps.BuildStepCompletion(context, node, "correlation-1", result);

        Assert.Equal(node.NodeId, completion.NodeId);
        Assert.Equal(node.NodeType, completion.NodeType);
        Assert.Equal(node.ArtifactKey, completion.ArtifactKey);
        Assert.Equal("correlation-1", completion.CorrelationId);
        Assert.Equal(result.OutputContractType, completion.OutputContractType);
        Assert.Equal(result.OutputHash, completion.OutputHash);
        Assert.Equal(0, completion.RetryCount);
        Assert.Equal(context.CurrentUtcDateTime, completion.CompletedAtUtc);
        Assert.Equal(result.ResolvedModel, completion.ResolvedModel);
        Assert.Equal("1.2.3+abc1234", completion.HostBuild);   // ADR-E15 pin set: copied from the recorded result, never orchestrator statics
    }

    [Fact]
    public void BuildSnapshot_ThreadsInitiatedByFromInput()
    {
        // ADR-E8/S13.19: the threshold identity rides every checkpoint — the root of the
        // derived-attribution chain for the execution's agent/tool actions.
        var context = new FakeTaskOrchestrationContext();
        var input = new GraphOrchestratorInput
        {
            Definition = OrchestrationFixtures.ThreeArtifactChain(),
            EngagementId = "eng-1",
            InitiatedBy = "user:oid-mark",
        };

        var snapshot = GraphOrchestratorSteps.BuildSnapshot(context, input, new GraphExecutionState(), ExecutionStatus.Running, currentNodeId: null);

        Assert.Equal("user:oid-mark", snapshot.InitiatedBy);
        Assert.Equal("eng-1", snapshot.EngagementId);
    }

    [Fact]
    public async Task RunNodeAsync_AppendsStepCompletion_AndSetsArtifactStatusToDraft()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition);
        var node = (AgentTaskNode)definition.Nodes[0];
        var state = new GraphExecutionState();

        await GraphOrchestratorSteps.RunNodeAsync(context, input, node, state, PolicyProvider);

        var completion = Assert.Single(state.CompletedSteps);
        Assert.Equal(node.NodeId, completion.NodeId);
        Assert.Equal($"{context.InstanceId}::{node.NodeId}::0", completion.CorrelationId);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses[node.ArtifactKey!]);
        Assert.Equal($"{context.InstanceId}:{node.ArtifactKey}:v1", state.SectionRefs[node.ArtifactKey!]);
    }

    [Fact]
    public async Task RunNodeAsync_NodeWithRetryPolicy_UsesItsProfileName()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition);
        var template = (AgentTaskNode)definition.Nodes[0];
        var node = template with { Retry = new RetryPolicySpec { ProfileName = "custom-profile" } };
        var state = new GraphExecutionState();
        var policyProvider = new FakeResiliencePolicyProvider();

        await GraphOrchestratorSteps.RunNodeAsync(context, input, node, state, policyProvider);

        Assert.Equal("custom-profile", policyProvider.RequestedProfileNames[0]);
    }

    [Fact]
    public async Task RunNodeAsync_NodeWithoutArtifactKey_DoesNotRecordArtifactStatus()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var input = OrchestrationFixtures.Input(definition);
        var template = (AgentTaskNode)definition.Nodes[0];
        var node = template with { NodeId = "scope-agent", ArtifactKey = null };
        var state = new GraphExecutionState();

        await GraphOrchestratorSteps.RunNodeAsync(context, input, node, state, PolicyProvider);

        Assert.Empty(state.ArtifactStatuses);
    }

    [Fact]
    public async Task RunInitialWalkAsync_ThreeArtifactChain_RunsNodesInTopologicalOrder()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["pricing"]);
    }

    [Fact]
    public async Task RunInitialWalkAsync_UnsupportedDefinition_Throws()
    {
        var context = new FakeTaskOrchestrationContext();
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.DispatcherModeChain());

        await Assert.ThrowsAsync<ContractViolationException>(() => GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier));
    }

    [Fact]
    public async Task ConsolidateAuditAsync_BuildsRequestFromInputAndStartedAtUtc_AndUsesSnapshotPersistenceProfile()
    {
        var context = new FakeTaskOrchestrationContext();
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var startedAtUtc = context.CurrentUtcDateTime;
        var policyProvider = new FakeResiliencePolicyProvider();
        ConsolidateAuditInput? capturedRequest = null;
        context.ActivityHandlers[WorkflowActivityNames.ConsolidateAuditActivity] = activityInput =>
        {
            capturedRequest = (ConsolidateAuditInput)activityInput!;
            return AuditFixtures.SignedRecord(capturedRequest);
        };

        await GraphOrchestratorSteps.ConsolidateAuditAsync(context, input, startedAtUtc, policyProvider);

        Assert.NotNull(capturedRequest);
        Assert.Equal(context.InstanceId, capturedRequest!.ExecutionId);
        Assert.Equal(input.Definition.DefinitionHash, capturedRequest.DefinitionHash);
        Assert.Equal(startedAtUtc, capturedRequest.StartedAtUtc);
        Assert.Equal(GraphOrchestratorSteps.SnapshotPersistenceProfile, Assert.Single(policyProvider.RequestedProfileNames));
    }

    [Fact]
    public async Task TryWaitForArtifactUpdateAsync_NoEventConfigured_ReturnsNull()
    {
        var context = new FakeTaskOrchestrationContext();

        var result = await GraphOrchestratorSteps.TryWaitForArtifactUpdateAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryWaitForArtifactUpdateAsync_EventConfigured_ReturnsChangedArtifact()
    {
        var context = new FakeTaskOrchestrationContext();
        context.ExternalEvents[GraphOrchestratorSteps.ArtifactUpdatedEventName] = "scope";

        var result = await GraphOrchestratorSteps.TryWaitForArtifactUpdateAsync(context);

        Assert.Equal("scope", result);
    }

    [Fact]
    public async Task EvaluateCascadeAsync_BuildsRequestFromState_AndReturnsActivityResponse()
    {
        var context = new FakeTaskOrchestrationContext();
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var state = new GraphExecutionState();
        state.ArtifactStatuses["scope"] = ArtifactStatus.Approved;
        state.ArtifactStatuses["approach"] = ArtifactStatus.Approved;
        state.ArtifactStatuses["pricing"] = ArtifactStatus.Approved;

        CascadeActivityRequest? capturedRequest = null;
        var expectedResponse = new CascadeActivityResponse
        {
            ChangedArtifact = "scope",
            DownstreamArtifacts = ["approach", "pricing"],
            SkippedArtifacts = [],
        };
        context.ActivityHandlers[WorkflowActivityNames.EvaluateCascadeActivity] = input =>
        {
            capturedRequest = (CascadeActivityRequest)input!;
            return expectedResponse;
        };

        var response = await GraphOrchestratorSteps.EvaluateCascadeAsync(context, definition, state, "scope");

        Assert.Same(expectedResponse, response);
        Assert.NotNull(capturedRequest);
        Assert.Same(definition, capturedRequest!.Definition);
        Assert.Equal(context.InstanceId, capturedRequest.Request.ExecutionId);
        Assert.Equal("scope", capturedRequest.Request.ChangedArtifact);
        Assert.Equal(state.ArtifactStatuses, capturedRequest.Request.CurrentArtifactStatuses);
    }

    [Fact]
    public async Task RegenerateDownstreamAsync_RunsAgentTaskNodesForDownstreamArtifacts()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var state = new GraphExecutionState();
        state.NodeOutputPayloads["scope-agent"] = "scope-payload";

        await GraphOrchestratorSteps.RegenerateDownstreamAsync(context, input, state, ["approach", "pricing"], PolicyProvider);

        Assert.Equal(["approach-agent", "pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
    }

    [Fact]
    public async Task RegenerateDownstreamAsync_ArtifactWithoutAgentTaskNode_IsSkipped()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var state = new GraphExecutionState();
        state.NodeOutputPayloads["approach-agent"] = "approach-payload";

        await GraphOrchestratorSteps.RegenerateDownstreamAsync(context, input, state, ["no-such-section", "pricing"], PolicyProvider);

        Assert.Equal(["pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
    }

    [Fact]
    public async Task RunCascadeWalkAsync_NoArtifactUpdatedEvent_LeavesStateUnchanged()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var state = new GraphExecutionState();

        await GraphOrchestratorSteps.RunCascadeWalkAsync(context, input, state, PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Empty(state.CompletedSteps);
    }

    [Fact]
    public async Task RunCascadeWalkAsync_ArtifactUpdatedEvent_EvaluatesCascadeAndRegeneratesDownstream()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        context.ExternalEvents[GraphOrchestratorSteps.ArtifactUpdatedEventName] = "scope";
        context.ActivityHandlers[WorkflowActivityNames.EvaluateCascadeActivity] = _ => new CascadeActivityResponse
        {
            ChangedArtifact = "scope",
            DownstreamArtifacts = ["approach", "pricing"],
            SkippedArtifacts = [],
        };
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain());
        var state = new GraphExecutionState();
        state.NodeOutputPayloads["scope-agent"] = "scope-payload";

        await GraphOrchestratorSteps.RunCascadeWalkAsync(context, input, state, PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(["approach-agent", "pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
    }

    [Fact]
    public async Task RunInitialWalkAsync_ChainWithBusinessGateApprove_RecordsApprovalsAndGateOccurrence()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        RegisterRequestApprovalActivity(context);
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-business-1")] = Decision(DecisionKind.Approve);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate());

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["pricing"]);
        Assert.Equal(state.SectionRefs["scope"], state.ApprovedSnapshotRefs["scope"]);
        Assert.Equal(1, state.GateOccurrences["gate-business-1"]);
        Assert.Equal(DecisionKind.Approve, Assert.Single(state.Decisions).Kind);
    }

    [Fact]
    public async Task RunInitialWalkAsync_ChainWithBusinessGateRejectThenApprove_RollsBackRegeneratesAndReapproves()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        RegisterRequestApprovalActivity(context);
        RegisterEvaluateCascadeActivity(context, "scope", ["approach", "pricing"]);
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-business-1")] = new Queue<object>(
        [
            Decision(DecisionKind.Reject, notes: "redo scope", rollbackToNodeId: "scope-agent"),
            Decision(DecisionKind.Approve),
        ]);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate());

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(
            ["scope-agent", "approach-agent", "pricing-agent", "scope-agent", "approach-agent", "pricing-agent"],
            state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal([DecisionKind.Reject, DecisionKind.Approve], state.Decisions.Select(decision => decision.Kind));
        Assert.Equal(2, state.GateOccurrences["gate-business-1"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["pricing"]);
        Assert.Equal($"{context.InstanceId}:scope:v2", state.ApprovedSnapshotRefs["scope"]);
    }

    [Fact]
    public async Task RunInitialWalkAsync_ChainWithBusinessGateRejectWithoutReapprove_ExitsWithoutReapproval()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        RegisterRequestApprovalActivity(context);
        RegisterEvaluateCascadeActivity(context, "scope", ["approach", "pricing"]);
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-business-1")] =
            Decision(DecisionKind.Reject, notes: "redo scope", rollbackToNodeId: "scope-agent");
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate(reapproveOnCascade: false));

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(
            ["scope-agent", "approach-agent", "pricing-agent", "scope-agent", "approach-agent", "pricing-agent"],
            state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(DecisionKind.Reject, Assert.Single(state.Decisions).Kind);
        Assert.Equal(1, state.GateOccurrences["gate-business-1"]);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["scope"]);
        Assert.Empty(state.ApprovedSnapshotRefs);
    }

    [Fact]
    public async Task RunInitialWalkAsync_ChainWithBusinessGateTimeout_EscalatesThenDecides()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterAgentTaskActivity(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        RegisterRequestApprovalActivity(context);
        var escalated = RegisterEscalateApprovalActivity(context);
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName("gate-business-1")] =
            new DecisionAfterTimeout(Decision(DecisionKind.Approve));
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate(timeoutMinutes: 2));

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);

        Assert.Equal(DecisionKind.Approve, Assert.Single(state.Decisions).Kind);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        Assert.Single(escalated);
    }

    [Fact]
    public async Task DecideAsync_HumanEscalatesThenDecides_ReturnsFinalDecisionAfterEscalating()
    {
        var context = new FakeTaskOrchestrationContext();
        var escalated = RegisterEscalateApprovalActivity(context);
        var gate = (HumanGateNode)OrchestrationFixtures.ChainWithBusinessGate().Nodes[3];
        context.ExternalEvents[GraphOrchestratorSteps.GateEventName(gate.NodeId)] = new Queue<object>(
        [
            Decision(DecisionKind.Escalate),
            Decision(DecisionKind.Approve),
        ]);
        var approvalRequest = BuildApprovalRequest(gate, occurrence: 0);

        var decision = await GraphOrchestratorSteps.DecideAsync(context, gate, approvalRequest);

        Assert.Equal(DecisionKind.Approve, decision.Kind);
        Assert.Single(escalated);
    }

    [Fact]
    public async Task OpenGateAsync_FirstEntry_BuildsRequestAtOccurrenceZeroAndIncrementsState()
    {
        var context = new FakeTaskOrchestrationContext();
        var requests = RegisterRequestApprovalActivity(context);
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var gate = (HumanGateNode)definition.Nodes[3];
        var input = OrchestrationFixtures.Input(definition);
        var state = new GraphExecutionState();
        state.SectionRefs["scope"] = "scope-ref-v1";

        await GraphOrchestratorSteps.OpenGateAsync(context, input, gate, state);

        var request = Assert.Single(requests);
        Assert.Equal(0, request.Occurrence);
        Assert.Equal("scope-ref-v1", request.SectionRefs["scope"]);
        Assert.Equal(gate.GateKind, request.GateKind);
        Assert.Equal(gate.ApproverRoles, request.ApproverRoles);
        Assert.Equal(gate.TimeoutMinutes, request.TimeoutMinutes);
        Assert.Equal(1, state.GateOccurrences[gate.NodeId]);
    }

    [Fact]
    public async Task OpenGateAsync_SecondEntry_OccurrenceReflectsPriorVisits()
    {
        var context = new FakeTaskOrchestrationContext();
        var requests = RegisterRequestApprovalActivity(context);
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var gate = (HumanGateNode)definition.Nodes[3];
        var input = OrchestrationFixtures.Input(definition);
        var state = new GraphExecutionState();
        state.GateOccurrences[gate.NodeId] = 1;

        await GraphOrchestratorSteps.OpenGateAsync(context, input, gate, state);

        Assert.Equal(1, Assert.Single(requests).Occurrence);
        Assert.Equal(2, state.GateOccurrences[gate.NodeId]);
    }

    [Fact]
    public void RecordApprovals_DraftArtifacts_BecomeApprovedAndCapturedInApprovedSnapshotRefs()
    {
        var state = new GraphExecutionState();
        state.ArtifactStatuses["scope"] = ArtifactStatus.Draft;
        state.ArtifactStatuses["approach"] = ArtifactStatus.Approved;
        state.SectionRefs["scope"] = "scope-ref-v1";
        state.SectionRefs["approach"] = "approach-ref-v1";

        GraphOrchestratorSteps.RecordApprovals(state);

        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["scope"]);
        Assert.Equal("scope-ref-v1", state.ApprovedSnapshotRefs["scope"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["approach"]);
        Assert.False(state.ApprovedSnapshotRefs.ContainsKey("approach"));
    }

    [Fact]
    public void ResolveRollbackTargetArtifact_ValidRollbackToNodeId_ReturnsArtifactKey()
    {
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var gate = (HumanGateNode)definition.Nodes[3];

        var section = GraphOrchestratorSteps.ResolveRollbackTargetArtifact(definition, gate);

        Assert.Equal("scope", section);
    }

    [Fact]
    public void ResolveRollbackTargetArtifact_NullRollbackToNodeId_ThrowsContractViolationException()
    {
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var gate = ((HumanGateNode)definition.Nodes[3]) with { RollbackToNodeId = null };

        var exception = Assert.Throws<ContractViolationException>(() => GraphOrchestratorSteps.ResolveRollbackTargetArtifact(definition, gate));

        Assert.Contains(gate.NodeId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreArtifactsAsync_EmptyRestoreSet_LeavesSectionRefsUnchanged()
    {
        var context = new FakeTaskOrchestrationContext();
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate());
        var state = new GraphExecutionState();
        state.SectionRefs["scope"] = "scope-ref-v1";

        await GraphOrchestratorSteps.RestoreArtifactsAsync(context, input, new Dictionary<string, string>(), state);

        Assert.Equal("scope-ref-v1", state.SectionRefs["scope"]);
    }

    [Fact]
    public async Task RestoreArtifactsAsync_NonEmptyRestoreSet_UpdatesSectionRefsFromResponse()
    {
        var context = new FakeTaskOrchestrationContext();
        RegisterRestoreArtifactsActivity(context);
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ChainWithBusinessGate());
        var state = new GraphExecutionState();
        state.SectionRefs["approach"] = "approach-ref-v2";
        var restoreSet = new Dictionary<string, string> { ["approach"] = "approach-ref-v1" };

        await GraphOrchestratorSteps.RestoreArtifactsAsync(context, input, restoreSet, state);

        Assert.Equal("approach-ref-v1", state.SectionRefs["approach"]);
    }

    [Fact]
    public void MarkInvalidSetRegenerating_SetsEachArtifactToRegenerating()
    {
        var state = new GraphExecutionState();
        state.ArtifactStatuses["scope"] = ArtifactStatus.Approved;
        state.ArtifactStatuses["approach"] = ArtifactStatus.Approved;
        state.ArtifactStatuses["pricing"] = ArtifactStatus.Approved;

        GraphOrchestratorSteps.MarkInvalidSetRegenerating(state, ["scope", "approach"]);

        Assert.Equal(ArtifactStatus.Regenerating, state.ArtifactStatuses["scope"]);
        Assert.Equal(ArtifactStatus.Regenerating, state.ArtifactStatuses["approach"]);
        Assert.Equal(ArtifactStatus.Approved, state.ArtifactStatuses["pricing"]);
    }

    [Fact]
    public async Task HandleRejectionAsync_RollbackToScope_RegeneratesInvalidSetWithRevisionNote()
    {
        var context = new FakeTaskOrchestrationContext();
        var capturedInputs = RegisterAgentTaskActivityCapturing(context);
        RegisterSnapshotStateActivity(context);
        RegisterArtifactStateActivity(context);
        RegisterEvaluateCascadeActivity(context, "scope", ["approach", "pricing"]);
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var gate = (HumanGateNode)definition.Nodes[3];
        var input = OrchestrationFixtures.Input(definition);
        var state = new GraphExecutionState();
        foreach (var section in new[] { "scope", "approach", "pricing" })
        {
            state.ArtifactStatuses[section] = ArtifactStatus.Draft;
            state.ArtifactVersions[section] = 1;
            state.SectionRefs[section] = $"{context.InstanceId}:{section}:v1";
        }

        var decision = Decision(DecisionKind.Reject, notes: "redo scope", rollbackToNodeId: "scope-agent");

        await GraphOrchestratorSteps.HandleRejectionAsync(context, input, gate, decision, state, new RollbackPlanner(), PolicyProvider);

        Assert.Equal(["scope-agent", "approach-agent", "pricing-agent"], state.CompletedSteps.Select(step => step.NodeId));
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["scope"]);
        Assert.All(capturedInputs, activityInput => Assert.Equal("redo scope", activityInput.RevisionNote));
    }

    /// <summary>Registers a handler over <see cref="FakeAgentTaskActivityPipeline"/>'s deterministic stub output, for tests that don't exercise the real S4.2 pipeline.</summary>
    private static void RegisterAgentTaskActivity(FakeTaskOrchestrationContext context) =>
        RegisterAgentTaskActivityCapturing(context);

    /// <summary>As <see cref="RegisterAgentTaskActivity"/>, but also returns every <see cref="AgentTaskActivityInput"/> the handler was called with, in call order.</summary>
    private static List<AgentTaskActivityInput> RegisterAgentTaskActivityCapturing(FakeTaskOrchestrationContext context)
    {
        var captured = new List<AgentTaskActivityInput>();
        context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = input =>
        {
            var activityInput = (AgentTaskActivityInput)input!;
            captured.Add(activityInput);
            return new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), activityInput).GetAwaiter().GetResult();
        };

        return captured;
    }

    /// <summary>Registers a handler mirroring ArtifactState's <c>SnapshotStateActivity</c> response shape (S2.4), for tests that don't assert on Cosmos persistence.</summary>
    private static void RegisterSnapshotStateActivity(FakeTaskOrchestrationContext context) =>
        context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = input =>
        {
            var snapshot = (ExecutionSnapshot)input!;
            return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
        };

    /// <summary>Registers a handler mirroring ArtifactState's <c>ArtifactStateActivity</c> response shape (S2.5), for tests that don't assert on Cosmos persistence.</summary>
    private static void RegisterArtifactStateActivity(FakeTaskOrchestrationContext context) =>
        context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = input =>
        {
            var request = (ArtifactStateActivityRequest)input!;
            return new ArtifactStateActivityResponse { SectionRef = $"{request.ExecutionId}:{request.ArtifactKey}:v{request.Version}" };
        };

    /// <summary>Registers a handler mirroring Hitl's <c>RequestApprovalActivity</c> response shape (doc 06 §9, <c>ApprovalRequestFactory.Open</c>), returning every <see cref="GateOpenRequest"/> the handler was called with, in call order.</summary>
    private static List<GateOpenRequest> RegisterRequestApprovalActivity(FakeTaskOrchestrationContext context)
    {
        var captured = new List<GateOpenRequest>();
        context.ActivityHandlers[WorkflowActivityNames.RequestApprovalActivity] = input =>
        {
            var request = (GateOpenRequest)input!;
            captured.Add(request);
            return BuildApprovalRequest(request);
        };

        return captured;
    }

    /// <summary>Registers a handler mirroring Hitl's <c>EscalateApprovalActivity</c> response shape (doc 06 §7), returning every <see cref="ApprovalRequest"/> the handler was called with, in call order.</summary>
    private static List<ApprovalRequest> RegisterEscalateApprovalActivity(FakeTaskOrchestrationContext context)
    {
        var captured = new List<ApprovalRequest>();
        context.ActivityHandlers[WorkflowActivityNames.EscalateApprovalActivity] = input =>
        {
            var request = (ApprovalRequest)input!;
            captured.Add(request);
            return request with { Status = ApprovalRequestStatus.Escalated };
        };

        return captured;
    }

    /// <summary>Registers a handler mirroring ArtifactState's <c>RestoreArtifactsActivity</c> response shape (doc 06 §6): the restored section's new ref is its requested <c>RestoreRef</c>.</summary>
    private static void RegisterRestoreArtifactsActivity(FakeTaskOrchestrationContext context) =>
        context.ActivityHandlers[WorkflowActivityNames.RestoreArtifactsActivity] = input =>
        {
            var request = (ArtifactRestoreActivityRequest)input!;
            return new ArtifactStateActivityResponse { SectionRef = request.RestoreRef };
        };

    /// <summary>Registers a handler mirroring CascadeLogic's <c>EvaluateCascadeActivity</c> response shape (S2.3), for tests that don't exercise the real cascade evaluator.</summary>
    private static void RegisterEvaluateCascadeActivity(FakeTaskOrchestrationContext context, string changedArtifact, IReadOnlyList<string> downstreamArtifacts) =>
        context.ActivityHandlers[WorkflowActivityNames.EvaluateCascadeActivity] = _ => new CascadeActivityResponse
        {
            ChangedArtifact = changedArtifact,
            DownstreamArtifacts = downstreamArtifacts,
            SkippedArtifacts = [],
        };

    /// <summary>Builds a <see cref="HitlDecision"/> for <c>gate-business-1</c> (S4.6 gate test fixtures).</summary>
    private static HitlDecision Decision(DecisionKind kind, string? notes = null, string? rollbackToNodeId = null) => new()
    {
        GateId = "gate-business-1",
        RequestId = "eng-1::wf-chain-gate:gate-business-1:0",
        ApproverId = "user:approver-1",
        Kind = kind,
        Notes = notes,
        RollbackToNodeId = rollbackToNodeId,
        DecidedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>Mirrors Hitl's <c>ApprovalRequestFactory.Open</c> (doc 06 §9), for tests that don't reference <c>Frontier.Platform.Hitl</c>'s internal factory.</summary>
    private static ApprovalRequest BuildApprovalRequest(GateOpenRequest request) => new()
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
        EscalateAtUtc = request.TimeoutMinutes > 0 ? request.RequestedAtUtc.AddMinutes(request.TimeoutMinutes) : null,
    };

    /// <summary>Builds a pending <see cref="ApprovalRequest"/> for <paramref name="gate"/> at <paramref name="occurrence"/>, for tests that call <see cref="GraphOrchestratorSteps.DecideAsync"/> directly.</summary>
    private static ApprovalRequest BuildApprovalRequest(HumanGateNode gate, int occurrence) => BuildApprovalRequest(new GateOpenRequest
    {
        ExecutionId = "eng-1::wf-chain-gate",
        EngagementId = "eng-1",
        GateId = gate.NodeId,
        GateKind = gate.GateKind,
        ApproverRoles = gate.ApproverRoles,
        SectionRefs = new Dictionary<string, string>(),
        Occurrence = occurrence,
        TimeoutMinutes = gate.TimeoutMinutes,
        RequestedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    });
}
