using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// Shared <see cref="WorkflowDefinition"/> fixtures for the S2.2 interpreter test suite.
/// Mirrors the shape of CascadeLogic's three-section chain fixture, duplicated here per
/// the library-boundaries rule (test projects don't reference each other's libraries).
/// </summary>
internal static class OrchestrationFixtures
{
    private const string PlaceholderHash = "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>A minimal Scope → Approach → Pricing chain of <see cref="AgentTaskNode"/>s, all <see cref="EdgeKind.Data"/> (S2.2 PoC seed). Execution mode is configurable for testing both OneShot and Dispatcher modes (S6.10).</summary>
    public static WorkflowDefinition ThreeArtifactChain(ExecutionMode? executionMode = null) => new()
    {
        WorkflowId = "three-section-chain",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Scope-Approach-Pricing chain",
        Nodes =
        [
            AgentTask("scope-agent", "scope", "ScopeRequest", "SummaryArtifact"),
            AgentTask("approach-agent", "approach", "SummaryArtifact", "PlanArtifact"),
            AgentTask("pricing-agent", "pricing", "PlanArtifact", "RateCardArtifact"),
        ],
        Edges =
        [
            new() { FromNodeId = "scope-agent", ToNodeId = "approach-agent", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
            new() { FromNodeId = "approach-agent", ToNodeId = "pricing-agent", Kind = EdgeKind.Data, ContractType = "PlanArtifact" },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = executionMode ?? ExecutionMode.OneShot,
    };

    /// <summary>The same chain, but with <see cref="WorkflowDefinition.Mode"/> set to <see cref="ExecutionMode.Dispatcher"/> — unsupported by the S2.2 PoC interpreter.</summary>
    public static WorkflowDefinition DispatcherModeChain() => ThreeArtifactChain(ExecutionMode.Dispatcher);

    /// <summary>Wraps <paramref name="definition"/> as <see cref="GraphOrchestratorInput"/> for the given <paramref name="engagementId"/> (S2.4 snapshot fixtures).</summary>
    public static GraphOrchestratorInput Input(WorkflowDefinition definition, string engagementId = "eng-1") => new()
    {
        Definition = definition,
        EngagementId = engagementId,
    };

    /// <summary>Two nodes joined by Data edges in both directions, forming a cycle — exercises <see cref="GraphTopology"/>'s cycle detection.</summary>
    public static WorkflowDefinition TwoNodeCycle() => new()
    {
        WorkflowId = "wf-cycle",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Two-node cycle fixture",
        Nodes =
        [
            AgentTask("scope-agent", "scope", "ScopeRequest", "SummaryArtifact"),
            AgentTask("approach-agent", "approach", "SummaryArtifact", "PlanArtifact"),
        ],
        Edges =
        [
            new() { FromNodeId = "scope-agent", ToNodeId = "approach-agent", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
            new() { FromNodeId = "approach-agent", ToNodeId = "scope-agent", Kind = EdgeKind.Data, ContractType = "PlanArtifact" },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>A chain with a <see cref="DecisionNode"/> inserted — unsupported by the S2.2/S4.6 PoC interpreter (only <see cref="AgentTaskNode"/>/<see cref="HumanGateNode"/> nodes are supported).</summary>
    public static WorkflowDefinition WithUnsupportedNode() => new()
    {
        WorkflowId = "wf-with-decision",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Chain with unsupported decision node",
        Nodes =
        [
            AgentTask("scope-agent", "scope", "ScopeRequest", "SummaryArtifact"),
            // S13.7j: DecisionNode became executable, so the still-unsupported ParallelNode
            // (deprecation-pending, ADR-5) carries this fixture's purpose now.
            new ParallelNode
            {
                NodeId = "branch-1",
                BranchNodeIds = ["scope-agent"],
                JoinNodeId = "scope-agent",
            },
        ],
        Edges =
        [
            new() { FromNodeId = "scope-agent", ToNodeId = "branch-1", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>
    /// The <see cref="ThreeArtifactChain"/> with a single <see cref="HumanGateNode"/> appended
    /// after pricing (doc 06 §13's PoC Gate 3 shape, S4.6): <c>RollbackToNodeId</c> points at
    /// <c>scope-agent</c>, matching the "redo scope" rejection scenario.
    /// </summary>
    public static WorkflowDefinition ChainWithBusinessGate(int timeoutMinutes = 0, bool reapproveOnCascade = true) => new()
    {
        WorkflowId = "wf-chain-gate",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Scope-Approach-Pricing chain with business gate",
        Nodes =
        [
            AgentTask("scope-agent", "scope", "ScopeRequest", "SummaryArtifact"),
            AgentTask("approach-agent", "approach", "SummaryArtifact", "PlanArtifact"),
            AgentTask("pricing-agent", "pricing", "PlanArtifact", "RateCardArtifact"),
            new HumanGateNode
            {
                NodeId = "gate-business-1",
                GateKind = GateKind.Business,
                ApproverRoles = ["business-approver"],
                PromptTemplate = "Review scope, approach, and pricing before they are finalised.",
                TimeoutMinutes = timeoutMinutes,
                RollbackToNodeId = "scope-agent",
                ReapproveOnCascade = reapproveOnCascade,
            },
        ],
        Edges =
        [
            new() { FromNodeId = "scope-agent", ToNodeId = "approach-agent", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
            new() { FromNodeId = "approach-agent", ToNodeId = "pricing-agent", Kind = EdgeKind.Data, ContractType = "PlanArtifact" },
            new() { FromNodeId = "pricing-agent", ToNodeId = "gate-business-1", Kind = EdgeKind.Control },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>
    /// ADR-5/S13.7i: an entry node fanning out to two independent branch nodes via Control
    /// edges, both converging on a join node — the shape the ready-set scheduler runs
    /// concurrently (branch node ids sort after the entry and before the join
    /// lexicographically, keeping assertions on scheduling order readable).
    /// </summary>
    public static WorkflowDefinition FanOutJoin() => new()
    {
        WorkflowId = "wf-fanout",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Fan-out/join over independent branches",
        Nodes =
        [
            AgentTask("a-entry", "brief", "ScopeRequest", "SummaryArtifact"),
            AgentTask("b-booking", "booking", "SummaryArtifact", "PlanArtifact"),
            AgentTask("b-ticket", "ticket", "SummaryArtifact", "PlanArtifact"),
            AgentTask("c-join", "confirm", "PlanArtifact", "RateCardArtifact"),
        ],
        Edges =
        [
            new() { FromNodeId = "a-entry", ToNodeId = "b-booking", Kind = EdgeKind.Control },
            new() { FromNodeId = "a-entry", ToNodeId = "b-ticket", Kind = EdgeKind.Control },
            new() { FromNodeId = "b-booking", ToNodeId = "c-join", Kind = EdgeKind.Control },
            new() { FromNodeId = "b-ticket", ToNodeId = "c-join", Kind = EdgeKind.Control },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>
    /// ADR-5 Decision 2 (gate barrier): two independent root branches where only
    /// <c>a-reviewed</c> feeds the gate — the gate must still wait for the unrelated
    /// <c>b-independent</c> branch to settle before opening.
    /// </summary>
    public static WorkflowDefinition ParallelBranchesGateOnOne() => new()
    {
        WorkflowId = "wf-gate-barrier",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Gate barrier over parallel branches",
        Nodes =
        [
            AgentTask("a-reviewed", "scope", "ScopeRequest", "SummaryArtifact"),
            AgentTask("b-independent", "approach", "ScopeRequest", "PlanArtifact"),
            new HumanGateNode
            {
                NodeId = "gate-scope",
                GateKind = GateKind.Business,
                ApproverRoles = ["business-approver"],
                PromptTemplate = "Review the scope.",
                TimeoutMinutes = 0,
                RollbackToNodeId = "a-reviewed",
                ReapproveOnCascade = false,
            },
        ],
        Edges =
        [
            new() { FromNodeId = "a-reviewed", ToNodeId = "gate-scope", Kind = EdgeKind.Control },
        ],
        DefinitionHash = PlaceholderHash,
        Mode = ExecutionMode.OneShot,
    };

    private static AgentTaskNode AgentTask(string nodeId, string sectionKey, string inputContractType, string outputContractType) => new()
    {
        NodeId = nodeId,
        ArtifactKey = sectionKey,
        Role = "analyst",
        InstructionsRef = $"instructions/{sectionKey}.md",
        InputContractType = inputContractType,
        OutputContractType = outputContractType,
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "analyst",
            BaselineComponents = ["firm-standards"],
            DynamicFields = [],
        },
    };

    /// <summary>
    /// The write classification the fixtures run under. Supplied by the test, not held by the
    /// engine: which tools mutate is deployment knowledge (<see cref="IMcpWriteClassifier"/>).
    /// </summary>
    internal static readonly IMcpWriteClassifier WriteClassifier = new FakeMcpWriteClassifier(
        "io.frontier.demo/autotask/update_ticket",
        "io.frontier.demo/teamreview/assign_resource_to_booking");
}
