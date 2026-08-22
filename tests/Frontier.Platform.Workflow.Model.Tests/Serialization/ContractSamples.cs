using System.Text.Json;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests.Serialization;

/// <summary>
/// One representative, fully-populated instance of every <see cref="IVersionedContract"/>
/// in this assembly, shared by the S1.6 round-trip / byte-stability / golden-file tests
/// (canonical-serialization skill).
/// </summary>
internal static class ContractSamples
{

    /// <summary>A well-formed <see cref="ContextRequest"/>, including the real-time tier.</summary>
    public static ContextRequest ContextRequest() => new()
    {
        EngagementId = "eng-1",
        AgentRole = "analyst",
        BaselineComponents = ["firm-standards", "playbooks"],
        DynamicFields = ["timeline", "stakeholders"],
        RequiresRealTime = true,
        RealTimeSources = ["crm-feed"],
    };

    /// <summary>
    /// S13.7j (ADR-5 D6): a definition whose <see cref="DecisionNode"/> carries the doc 14
    /// §6 branch tree — pins the wire shape of both predicate discriminators (<c>field</c>/
    /// <c>logical</c>) and the snake_case operator enums.
    /// </summary>
    public static WorkflowDefinition WorkflowDefinitionDecisionBranches() => new()
    {
        WorkflowId = "wf-decision-branches",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Decision with branch tree",
        Nodes =
        [
            new DecisionNode
            {
                NodeId = "route-decision",
                DefaultBranchNodeId = "low-path",
                Branches =
                [
                    new ConditionalBranch
                    {
                        TargetNodeId = "high-path",
                        Condition = new LogicalPredicate
                        {
                            Op = LogicalOp.And,
                            Operands =
                            [
                                new FieldComparisonPredicate { FieldPath = "scope.title", Operator = ComparisonOp.StartsWith, Value = "URGENT" },
                                new LogicalPredicate
                                {
                                    Op = LogicalOp.Not,
                                    Operands = [new FieldComparisonPredicate { FieldPath = "approach.cost_estimate", Operator = ComparisonOp.Lte, Value = "1000.00" }],
                                },
                            ],
                        },
                    },
                    new ConditionalBranch
                    {
                        TargetNodeId = "review-path",
                        Condition = new FieldComparisonPredicate { FieldPath = "scope.title", Operator = ComparisonOp.In, Values = ["review", "audit"] },
                    },
                ],
            },
        ],
        Edges = [],
        DefinitionHash = new string('0', 124),
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>S13.7j: a snapshot that skipped an unselected decision subtree — pins <c>skipped_node_ids</c> and the decision step's <c>selected_branch_node_id</c> wire shape.</summary>
    public static ExecutionSnapshot ExecutionSnapshotSkipped() => ExecutionSnapshot() with
    {
        SkippedNodeIds = ["low-path"],
        CompletedSteps =
        [
            new StepCompletion
            {
                NodeId = "route-decision",
                NodeType = NodeType.Decision,
                ArtifactKey = null,
                CorrelationId = "E2E::Acme::Admin-Website::wf-1::route-decision::0",
                OutputContractType = string.Empty,
                OutputHash = string.Empty,
                RetryCount = 0,
                CompletedAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
                SelectedBranchNodeId = "high-path",
            },
        ],
    };

    /// <summary>A well-formed <see cref="ExecutionSnapshot"/> paused at a human gate.</summary>
    public static ExecutionSnapshot ExecutionSnapshot() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 3,
        Status = ExecutionStatus.PausedAtGate,
        CurrentNodeId = "human-gate",
        PausedAtGateId = "human-gate",
        Artifacts = new Dictionary<string, ArtifactStatus> { ["scope"] = ArtifactStatus.Approved, ["approach"] = ArtifactStatus.Draft },
        CompletedSteps =
        [
            new StepCompletion
            {
                NodeId = "scope-agent",
                NodeType = NodeType.AgentTask,
                ArtifactKey = "scope",
                CorrelationId = "corr-1",
                OutputContractType = "ScopeSection",
                OutputHash = "abc123",
                RetryCount = 0,
                CompletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
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
                DecidedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            },
        ],
        ApprovedSnapshotRefs = new Dictionary<string, string> { ["scope"] = "scope-snapshot-1" },
        CheckpointedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>S9.45: a well-formed <see cref="ExecutionSnapshot"/> paused on a permanent step failure — the new <c>failure_classification</c> field's wire shape.</summary>
    public static ExecutionSnapshot ExecutionSnapshotPausedOnFailure() => new()
    {
        ExecutionId = "eng-1::wf-1",
        EngagementId = "eng-1",
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        Sequence = 2,
        Status = ExecutionStatus.PausedOnFailure,
        CurrentNodeId = "gen-scope",
        FailureClassification = "contract_violation",
        Artifacts = new Dictionary<string, ArtifactStatus>(),
        CompletedSteps = [],
        Decisions = [],
        ApprovedSnapshotRefs = new Dictionary<string, string>(),
        CheckpointedAtUtc = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="WorkflowDefinition"/> exercising all eight <see cref="WorkflowNode"/> subtypes.</summary>
    public static WorkflowDefinition WorkflowDefinition() => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Scope-Approach-Pricing seed",
        Nodes =
        [
            new AgentTaskNode
            {
                NodeId = "scope-agent",
                ArtifactKey = "scope",
                Retry = new RetryPolicySpec { ProfileName = "llm-default", MaxAttemptsOverride = 2, TimeoutSecondsOverride = 30 },
                Role = "analyst",
                InstructionsRef = "instructions/scope.md",
                InputContractType = "ScopeRequest",
                OutputContractType = "ScopeSection",
                ContextRequest = ContextRequest(),
                ToolRefs = ["io.frontier.demo/autotask/get_new_ticket"],
            },
            new CascadeCheckNode
            {
                NodeId = "cascade-check",
                TriggerArtifactKeys = ["scope"],
            },
#pragma warning disable CS0618 // Legacy string-predicate wire shape stays covered until the phase boundary removes it (S13.7j).
            new DecisionNode
            {
                NodeId = "route-decision",
                Predicate = "scope.objectives.count > 0",
                DefaultBranchNodeId = "parallel-1",
            },
#pragma warning restore CS0618
            new ParallelNode
            {
                NodeId = "parallel-1",
                BranchNodeIds = ["loop-1", "mcp-1"],
                JoinNodeId = "human-gate",
            },
            new LoopNode
            {
                NodeId = "loop-1",
                BodyNodeId = "mcp-1",
                MaxIterations = 3,
            },
            new McpToolNode
            {
                NodeId = "mcp-1",
                ToolRef = "com.example/crm/create_opportunity",
                TimeoutSeconds = 30,
                IdempotencyKeySpec = "engagementId+sectionKey",
            },
            new HumanGateNode
            {
                NodeId = "human-gate",
                GateKind = GateKind.Business,
                ApproverRoles = ["partner"],
                PromptTemplate = "Approve pricing for this engagement?",
                TimeoutMinutes = 60,
                RollbackToNodeId = "scope-agent",
            },
#pragma warning disable CS0618 // Exercises the deprecated wire shape for backward compatibility (ADR-CR1).
            new ContextInjectionNode
            {
                NodeId = "context-injection",
                ContextRequest = ContextRequest(),
            },
#pragma warning restore CS0618
        ],
        Edges =
        [
            new WorkflowEdge { FromNodeId = "scope-agent", ToNodeId = "cascade-check", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "scope-agent", ToNodeId = "route-decision", Kind = EdgeKind.Data, ContractType = "ScopeSection" },
            new WorkflowEdge { FromNodeId = "cascade-check", ToNodeId = "route-decision", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "route-decision", ToNodeId = "parallel-1", Kind = EdgeKind.Control, Condition = "default" },
            new WorkflowEdge { FromNodeId = "parallel-1", ToNodeId = "loop-1", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "parallel-1", ToNodeId = "mcp-1", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "loop-1", ToNodeId = "human-gate", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "mcp-1", ToNodeId = "human-gate", Kind = EdgeKind.Control },
        ],
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        Mode = ExecutionMode.OneShot,
    };

    /// <summary>A well-formed <see cref="ResolvedModelSummary"/> (doc 08 §6).</summary>
    public static ResolvedModelSummary ResolvedModelSummary() => new()
    {
        RoleId = "deep-reasoning",
        Provider = "anthropic",
        ModelId = "claude-fable-5",
        ModelVersion = "2026-05-01",
        ChainPosition = 0,
        MappingVersion = 1,
    };

    /// <summary>A well-formed <see cref="ConsolidateAuditInput"/> (S5.4 activity input).</summary>
    public static ConsolidateAuditInput ConsolidateAuditInput() => new()
    {
        ExecutionId = "eng-1::wf-1",
        DefinitionHash = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
        StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>A well-formed <see cref="PayloadRef"/> (ADR-E1, S13.1).</summary>
    public static PayloadRef PayloadRef() => new()
    {
        StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/SUB-001/upload/products.xlsx"),
        ContentHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        SizeBytes = 1048576,
    };

    /// <summary>S13.19: a snapshot carrying the execution's directing human (ADR-E8) — new golden, existing snapshot goldens untouched.</summary>
    public static ExecutionSnapshot ExecutionSnapshotInitiated() => ExecutionSnapshot() with { InitiatedBy = "user:oid-mark" };

    /// <summary>S13.17: a snapshot whose completed step carries the host-build stamp (ADR-E15 pin set) — new golden, existing snapshot goldens untouched.</summary>
    public static ExecutionSnapshot ExecutionSnapshotWithHostBuild()
    {
        var baseline = ExecutionSnapshot();
        return baseline with
        {
            CompletedSteps = [baseline.CompletedSteps[0] with { HostBuild = "1.2.3+abc1234" }],
        };
    }

    /// <summary>A well-formed ref-mode <see cref="TypedPayload"/> with facts (ADR-E2, S13.2).</summary>
    public static TypedPayload TypedPayloadByRef() => new()
    {
        SchemaRef = "schemas/record-batch/1.0",
        PayloadRef = new PayloadRef
        {
            StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/SUB-001/extract/rows.json"),
            ContentHash = "b5bb9d8014a0f9b1d61e21e796d78dccdf1352f23cd32812f4850b878ae4944c",
            ContentType = "application/json",
            SizeBytes = 7340032,
        },
        Facts = Json("""{"row_count":4812,"sheet_count":3}"""),
    };

    /// <summary>A well-formed inline-mode <see cref="TypedPayload"/> (ADR-E2, S13.2).</summary>
    public static TypedPayload TypedPayloadInline() => new()
    {
        SchemaRef = "schemas/classification-result/1.0",
        Payload = Json("""{"category":"product_upload","confidence":0.97}"""),
    };

    /// <summary>Parses <paramref name="json"/> to a detached <see cref="JsonElement"/>.</summary>
    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
