using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.7j (ADR-5 Decision 6) walk tests: a <see cref="DecisionNode"/> evaluates its doc 14
/// §6 branch tree purely in the body, routes to the first matching branch (default
/// otherwise), kills the unselected subtrees (recorded as skipped in the snapshot), and a
/// join with mixed dead/live inbound edges still runs. Gates inside a dead branch never
/// open.
/// </summary>
public sealed class GraphDecisionWalkTests
{
    private static readonly FakeResiliencePolicyProvider PolicyProvider = new();

    [Fact]
    public async Task Decision_FirstMatchingBranch_RoutesAndSkipsUnselectedSubtree()
    {
        var harness = new DecisionHarness(RoutedDefinition(), entryTitle: "HIGH");

        var state = await harness.RunWalkAsync();

        Assert.Contains("x-high", harness.StartedNodes);
        Assert.DoesNotContain("y-low", harness.StartedNodes);
        Assert.Contains("z-end", harness.StartedNodes); // mixed inbound: dead y-low edge + live x-high edge
        Assert.Equal(["y-low"], state.SkippedNodeIds);

        var decisionStep = Assert.Single(state.CompletedSteps, step => step.NodeId == "d-route");
        Assert.Equal(NodeType.Decision, decisionStep.NodeType);
        Assert.Equal("x-high", decisionStep.SelectedBranchNodeId);
        Assert.EndsWith("::d-route::0", decisionStep.CorrelationId, StringComparison.Ordinal);

        var final = harness.Snapshots[^1];
        Assert.Equal(["y-low"], final.SkippedNodeIds);
    }

    [Fact]
    public async Task Decision_NoConditionMatches_TakesDefaultBranch()
    {
        var harness = new DecisionHarness(RoutedDefinition(), entryTitle: "routine");

        var state = await harness.RunWalkAsync();

        Assert.Contains("y-low", harness.StartedNodes);
        Assert.DoesNotContain("x-high", harness.StartedNodes);
        Assert.Equal(["x-high"], state.SkippedNodeIds);
        Assert.Equal("y-low", Assert.Single(state.CompletedSteps, s => s.NodeId == "d-route").SelectedBranchNodeId);
    }

    [Fact]
    public async Task Decision_GateInDeadBranch_IsSkippedWithoutOpening()
    {
        var definition = RoutedDefinition() with
        {
            Nodes =
            [
                Agent("a-entry", "scope", "SummaryArtifact"),
                Decision("d-route", defaultTarget: "y-low", branchTarget: "g-review"),
                new HumanGateNode
                {
                    NodeId = "g-review",
                    GateKind = GateKind.Business,
                    ApproverRoles = ["business-approver"],
                    PromptTemplate = "Review the high-risk path.",
                    TimeoutMinutes = 0,
                    RollbackToNodeId = "a-entry",
                    ReapproveOnCascade = false,
                },
                Agent("y-low", "low", "PlanArtifact"),
            ],
            Edges =
            [
                Edge("a-entry", "d-route"),
                Edge("d-route", "g-review"),
                Edge("d-route", "y-low"),
            ],
        };
        var harness = new DecisionHarness(definition, entryTitle: "routine"); // default branch → gate branch dies

        var state = await harness.RunWalkAsync();

        Assert.Empty(harness.GateOpenings);
        Assert.Equal(["g-review"], state.SkippedNodeIds);
        Assert.Contains("y-low", harness.StartedNodes);
    }

    [Fact]
    public async Task Decision_WithoutBranches_ThrowsContractViolation()
    {
        var definition = RoutedDefinition() with
        {
            Nodes =
            [
                Agent("a-entry", "scope", "SummaryArtifact"),
                new DecisionNode { NodeId = "d-route", DefaultBranchNodeId = "y-low" },
                Agent("y-low", "low", "PlanArtifact"),
            ],
            Edges = [Edge("a-entry", "d-route"), Edge("d-route", "y-low")],
        };
        var harness = new DecisionHarness(definition, entryTitle: "any");

        var exception = await Assert.ThrowsAsync<ContractViolationException>(() => harness.RunWalkAsync());

        Assert.Contains("declares no branches", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>a-entry → d-route ─┬→ x-high ─┬→ z-end; branch selects x-high when scope.title == "HIGH", default y-low.</summary>
    private static WorkflowDefinition RoutedDefinition() => new()
    {
        WorkflowId = "wf-decision",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Decision routing",
        Nodes =
        [
            Agent("a-entry", "scope", "SummaryArtifact"),
            Decision("d-route", defaultTarget: "y-low", branchTarget: "x-high"),
            Agent("x-high", "high", "PlanArtifact"),
            Agent("y-low", "low", "PlanArtifact"),
            Agent("z-end", "end", "RateCardArtifact"),
        ],
        Edges =
        [
            Edge("a-entry", "d-route"),
            Edge("d-route", "x-high"),
            Edge("d-route", "y-low"),
            Edge("x-high", "z-end"),
            Edge("y-low", "z-end"),
        ],
        DefinitionHash = new string('0', 124),
        Mode = ExecutionMode.OneShot,
    };

    private static DecisionNode Decision(string id, string defaultTarget, string branchTarget) => new()
    {
        NodeId = id,
        DefaultBranchNodeId = defaultTarget,
        Branches =
        [
            new ConditionalBranch
            {
                TargetNodeId = branchTarget,
                Condition = new FieldComparisonPredicate { FieldPath = "scope.title", Operator = ComparisonOp.Eq, Value = "HIGH" },
            },
        ],
    };

    private static AgentTaskNode Agent(string id, string sectionKey, string outputContract) => new()
    {
        NodeId = id,
        ArtifactKey = sectionKey,
        Role = "analyst",
        InstructionsRef = $"instructions/{sectionKey}.md",
        InputContractType = "ScopeRequest",
        OutputContractType = outputContract,
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        },
    };

    private static WorkflowEdge Edge(string from, string to) => new() { FromNodeId = from, ToNodeId = to, Kind = EdgeKind.Control };

    /// <summary>Runs a walk whose agents emit JSON payloads (predicates parse section outputs); the entry's <c>title</c> field is test-controlled.</summary>
    private sealed class DecisionHarness
    {
        private readonly WorkflowDefinition _definition;

        public DecisionHarness(WorkflowDefinition definition, string entryTitle)
        {
            _definition = definition;
            Context = new FakeTaskOrchestrationContext();
            Context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = input =>
            {
                var activityInput = (AgentTaskActivityInput)input!;
                StartedNodes.Add(activityInput.NodeId);
                var payload = activityInput.NodeId == "a-entry"
                    ? $$"""{"schema_version":"1.0","title":{{System.Text.Json.JsonSerializer.Serialize(entryTitle)}},"objectives":[]}"""
                    : """{"schema_version":"1.0"}""";
                return new AgentTaskActivityResult
                {
                    NodeId = activityInput.NodeId,
                    ArtifactKey = activityInput.ArtifactKey,
                    OutputContractType = activityInput.OutputContractType,
                    OutputPayload = payload,
                    OutputHash = CanonicalProfile.Hash(payload),
                    ResolvedModel = new ResolvedModelSummary { RoleId = "analyst", Provider = "anthropic", ModelId = "claude-fable-5", ChainPosition = 0, MappingVersion = 1 },
                };
            };
            Context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = input =>
            {
                var snapshot = (ExecutionSnapshot)input!;
                Snapshots.Add(snapshot);
                return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
            };
            Context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = input =>
            {
                var request = (ArtifactStateActivityRequest)input!;
                return new ArtifactStateActivityResponse { SectionRef = $"{request.ExecutionId}:{request.ArtifactKey}:v{request.Version}" };
            };
            Context.ActivityHandlers[WorkflowActivityNames.RequestApprovalActivity] = input =>
            {
                var request = (GateOpenRequest)input!;
                GateOpenings.Add(request);
                return ApprovalRequestFactory.Open(request);
            };
        }

        public FakeTaskOrchestrationContext Context { get; }
        public List<string> StartedNodes { get; } = [];
        public List<ExecutionSnapshot> Snapshots { get; } = [];
        public List<GateOpenRequest> GateOpenings { get; } = [];

        public Task<GraphExecutionState> RunWalkAsync() =>
            GraphOrchestratorSteps.RunInitialWalkAsync(Context, OrchestrationFixtures.Input(_definition), new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);
    }
}
