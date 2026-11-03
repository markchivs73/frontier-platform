using System.Reflection;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// GOV-02: <see cref="NodeDiffService"/> compares each shared node's canonical bytes, so an edit
/// to any node field is reported as modified. One case per serialized field of every
/// <see cref="WorkflowNode"/> subtype (plus the base fields), guarded by a reflection test so a
/// field added later without a case fails here rather than going silently undetected.
/// </summary>
public sealed class NodeDiffServiceNodeTypeTests
{
    /// <summary>Field edits keyed <c>{node_type}.{wire_name}</c>; base-record fields use <c>node.{wire_name}</c>.</summary>
    private static readonly Dictionary<string, (WorkflowNode From, WorkflowNode To)> FieldEdits = new(StringComparer.Ordinal)
    {
        ["node.artifact_key"] = (Agent(), Agent() with { ArtifactKey = "pricing" }),
        ["node.retry"] = (Agent(), Agent() with { Retry = new RetryPolicySpec { ProfileName = "llm-standard" } }),

        ["agent_task.role"] = (Agent(), Agent() with { Role = "other-role" }),
        ["agent_task.instructions_ref"] = (Agent(), Agent() with { InstructionsRef = "instructions-v2" }),
        ["agent_task.input_contract_type"] = (Agent(), Agent() with { InputContractType = "In2" }),
        ["agent_task.output_contract_type"] = (Agent(), Agent() with { OutputContractType = "Out2" }),
        ["agent_task.context_request"] = (Agent(), Agent() with { ContextRequest = Context("field-b") }),
        ["agent_task.tool_refs"] = (Agent(), Agent() with { ToolRefs = ["tool-a"] }),
        ["agent_task.tool_contracts"] = (Agent(), Agent() with { ToolContracts = [new AgentToolContract { ToolRef = "tool-a" }] }),

        ["human_gate.gate_kind"] = (Gate(), Gate() with { GateKind = GateKind.Technical }),
        ["human_gate.approver_roles"] = (Gate(), Gate() with { ApproverRoles = ["business-approver", "partner"] }),
        ["human_gate.prompt_template"] = (Gate(), Gate() with { PromptTemplate = "Approve now?" }),
        ["human_gate.timeout_minutes"] = (Gate(), Gate() with { TimeoutMinutes = 60 }),
        ["human_gate.rollback_to_node_id"] = (Gate(), Gate() with { RollbackToNodeId = "node-0" }),
        ["human_gate.reapprove_on_cascade"] = (Gate(), Gate() with { ReapproveOnCascade = false }),

#pragma warning disable CS0618 // Legacy string predicate is still a wire field until the phase boundary (S13.7j).
        ["decision.predicate"] = (Decision(), Decision() with { Predicate = "a > 2" }),
#pragma warning restore CS0618
        ["decision.default_branch_node_id"] = (Decision(), Decision() with { DefaultBranchNodeId = "node-3" }),
        ["decision.branches"] = (Decision(), Decision() with { Branches = [Branch("node-3")] }),

        ["parallel.branch_node_ids"] = (Parallel(), Parallel() with { BranchNodeIds = ["b1", "b2"] }),
        ["parallel.join_node_id"] = (Parallel(), Parallel() with { JoinNodeId = "node-join-2" }),

        ["loop.body_node_id"] = (Loop(), Loop() with { BodyNodeId = "node-body-2" }),
        ["loop.max_iterations"] = (Loop(), Loop() with { MaxIterations = 5 }),

        ["mcp_tool.tool_ref"] = (Tool(), Tool() with { ToolRef = "tool-b" }),
        ["mcp_tool.timeout_seconds"] = (Tool(), Tool() with { TimeoutSeconds = 60 }),
        ["mcp_tool.idempotency_key_spec"] = (Tool(), Tool() with { IdempotencyKeySpec = "spec-2" }),
        ["mcp_tool.input_contract_type"] = (Tool(), Tool() with { InputContractType = "ToolIn" }),
        ["mcp_tool.output_contract_type"] = (Tool(), Tool() with { OutputContractType = "ToolOut" }),
        ["mcp_tool.writes"] = (Tool(), Tool() with { Writes = true }),

        ["cascade_check.trigger_artifact_keys"] = (Cascade(), Cascade() with { TriggerArtifactKeys = ["scope", "pricing"] }),

#pragma warning disable CS0618 // Obsolete by design (ADR-CR1) — still a valid wire shape, so its edits must diff too.
        ["context_injection.context_request"] = (ContextInjection(), ContextInjection() with { ContextRequest = Context("field-b") }),
#pragma warning restore CS0618
    };

    /// <summary>The field-edit case names, for the theory.</summary>
    public static TheoryData<string> FieldNames => new(FieldEdits.Keys);

    [Theory]
    [MemberData(nameof(FieldNames))]
    public void Compute_EditToOneNodeField_IsReportedAsModified(string field)
    {
        var (from, to) = FieldEdits[field];

        var diff = new NodeDiffService().Compute(DefinitionWith(from), DefinitionWith(to));

        Assert.Equal(["node-1"], diff.NodesModified);
    }

    [Fact]
    public void FieldEdits_CoverEverySerializedFieldOfEveryNodeType()
    {
        var expected = WireFields(typeof(WorkflowNode), "node")
            .Concat(typeof(WorkflowNode).GetCustomAttributes<JsonDerivedTypeAttribute>()
                .SelectMany(d => WireFields(d.DerivedType, (string)d.TypeDiscriminator!)))
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, FieldEdits.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Compute_EqualButSeparatelyBuiltNodes_AreNotModified()
    {
        var diff = new NodeDiffService().Compute(DefinitionWith(Agent()), DefinitionWith(Agent()));

        Assert.Empty(diff.NodesModified);
    }

    [Fact]
    public void Compute_NodeTypeChangedForSameId_IsReportedAsModified()
    {
        var diff = new NodeDiffService().Compute(DefinitionWith(Gate()), DefinitionWith(Loop()));

        Assert.Equal(["node-1"], diff.NodesModified);
    }

    private static IEnumerable<string> WireFields(Type type, string prefix) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(name => name is not null && name != "node_id")
            .Select(name => $"{prefix}.{name}");

    private static WorkflowDefinition DefinitionWith(WorkflowNode node) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "test-type",
        Name = "Test",
        Nodes = [node],
        Edges = [],
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    private static ContextRequest Context(string dynamicField) => new()
    {
        EngagementId = "eng-1",
        AgentRole = "deep-reasoning",
        BaselineComponents = [],
        DynamicFields = [dynamicField],
    };

    private static AgentTaskNode Agent() => new()
    {
        NodeId = "node-1",
        Role = "deep-reasoning",
        InstructionsRef = "instructions-v1",
        InputContractType = "In",
        OutputContractType = "Out",
        ContextRequest = Context("field-a"),
    };

    private static HumanGateNode Gate() => new()
    {
        NodeId = "node-1",
        GateKind = GateKind.Business,
        ApproverRoles = ["business-approver"],
        PromptTemplate = "Approve?",
        TimeoutMinutes = 30,
    };

#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).
    private static DecisionNode Decision() => new()
    {
        NodeId = "node-1",
        Predicate = "a > 1",
        DefaultBranchNodeId = "node-2",
    };
#pragma warning restore CS0618

    private static ConditionalBranch Branch(string target) => new()
    {
        TargetNodeId = target,
        Condition = new FieldComparisonPredicate { FieldPath = "scope.title", Operator = ComparisonOp.Eq, Value = "HIGH" },
    };

    private static ParallelNode Parallel() => new()
    {
        NodeId = "node-1",
        BranchNodeIds = ["b1"],
        JoinNodeId = "node-join",
    };

    private static LoopNode Loop() => new()
    {
        NodeId = "node-1",
        BodyNodeId = "node-body",
        MaxIterations = 3,
    };

    private static McpToolNode Tool() => new()
    {
        NodeId = "node-1",
        ToolRef = "tool-a",
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "spec",
    };

    private static CascadeCheckNode Cascade() => new()
    {
        NodeId = "node-1",
        TriggerArtifactKeys = ["scope"],
    };

#pragma warning disable CS0618 // Obsolete by design (ADR-CR1) — still a valid WorkflowNode subtype.
    private static ContextInjectionNode ContextInjection() => new()
    {
        NodeId = "node-1",
        ContextRequest = Context("field-a"),
    };
#pragma warning restore CS0618
}
