using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.7c walk tests: an <see cref="McpToolNode"/> runs in the concurrent frontier via
/// <c>InvokeMcpToolActivity</c> — correlation-id idempotency keys for writes, default
/// <c>mcp-write</c>/<c>mcp-read</c> retry profiles by write classification, section and
/// snapshot recording, and results feeding downstream data edges.
/// </summary>
public sealed class GraphMcpToolWalkTests
{
    [Fact]
    public async Task WriteToolNode_RunsWithIdempotencyKeyAndWriteProfile()
    {
        var harness = new McpHarness(Definition(toolRef: "com.example.crm/tickets/update_ticket"));

        var state = await harness.RunWalkAsync();

        var activityInput = Assert.Single(harness.ToolInputs);
        Assert.Equal("com.example.crm/tickets/update_ticket", activityInput.ToolRef);
        Assert.EndsWith("::t-tool::0", activityInput.CorrelationId, StringComparison.Ordinal);
        Assert.Equal(activityInput.CorrelationId, activityInput.IdempotencyKey); // write ⇒ key = correlation id
        Assert.Equal(45, activityInput.TimeoutSeconds);
        Assert.Equal("""{"schema_version":"1.0"}""", activityInput.InputPayload); // the upstream Data-edge payload

        Assert.Contains(GraphOrchestratorSteps.McpWriteProfile, harness.PolicyProvider.RequestedProfileNames);
        var step = Assert.Single(state.CompletedSteps, s => s.NodeId == "t-tool");
        Assert.Equal(NodeType.McpTool, step.NodeType);
        Assert.Equal(CanonicalProfile.Hash("""{"updated":true}"""), step.OutputHash);
        Assert.Equal("""{"updated":true}""", state.NodeOutputPayloads["t-tool"]);
        Assert.Equal(ArtifactStatus.Draft, state.ArtifactStatuses["ticket-update"]);
    }

    [Fact]
    public async Task ReadToolNode_NoIdempotencyKey_ReadProfile()
    {
        var harness = new McpHarness(Definition(toolRef: "com.example.crm/tickets/get_new_ticket"));

        await harness.RunWalkAsync();

        Assert.Null(Assert.Single(harness.ToolInputs).IdempotencyKey);
        Assert.Contains(GraphOrchestratorSteps.McpReadProfile, harness.PolicyProvider.RequestedProfileNames);
    }

    [Fact]
    public async Task NodeRetryProfile_OverridesTheDefault()
    {
        var definition = Definition(toolRef: "com.example.crm/tickets/update_ticket");
        definition = definition with
        {
            Nodes = [.. definition.Nodes.Select(n => n is McpToolNode tool ? tool with { Retry = new RetryPolicySpec { ProfileName = "custom-mcp" } } : n)],
        };
        var harness = new McpHarness(definition);

        await harness.RunWalkAsync();

        Assert.Contains("custom-mcp", harness.PolicyProvider.RequestedProfileNames);
    }

    [Fact]
    public async Task ToolResult_FeedsDownstreamDataEdge()
    {
        // a-entry → t-tool → z-after with data edges throughout: the agent after the tool
        // receives the tool's result as its upstream payload.
        var definition = Definition(toolRef: "com.example.crm/tickets/get_new_ticket") with
        {
            Nodes =
            [
                Agent("a-entry", "scope", "SummaryArtifact"),
                Tool("t-tool", "com.example.crm/tickets/get_new_ticket"),
                Agent("z-after", "after", "PlanArtifact"),
            ],
            Edges =
            [
                new() { FromNodeId = "a-entry", ToNodeId = "t-tool", Kind = EdgeKind.Control },
                new() { FromNodeId = "a-entry", ToNodeId = "t-tool", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
                new() { FromNodeId = "t-tool", ToNodeId = "z-after", Kind = EdgeKind.Control },
                new() { FromNodeId = "t-tool", ToNodeId = "z-after", Kind = EdgeKind.Data, ContractType = "LookupResult" },
            ],
        };
        var harness = new McpHarness(definition);

        await harness.RunWalkAsync();

        Assert.Equal("""{"updated":true}""", harness.AgentUpstreamPayloads["z-after"]);
    }

    private static WorkflowDefinition Definition(string toolRef) => new()
    {
        WorkflowId = "wf-mcp",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "Deterministic tool step",
        Nodes =
        [
            Agent("a-entry", "scope", "SummaryArtifact"),
            Tool("t-tool", toolRef),
        ],
        Edges =
        [
            new() { FromNodeId = "a-entry", ToNodeId = "t-tool", Kind = EdgeKind.Control },
            new() { FromNodeId = "a-entry", ToNodeId = "t-tool", Kind = EdgeKind.Data, ContractType = "SummaryArtifact" },
        ],
        DefinitionHash = new string('0', 124),
        Mode = ExecutionMode.OneShot,
    };

    private static McpToolNode Tool(string id, string toolRef) => new()
    {
        NodeId = id,
        ArtifactKey = "ticket-update",
        ToolRef = toolRef,
        TimeoutSeconds = 45,
        IdempotencyKeySpec = "correlation",
    };

    private static AgentTaskNode Agent(string id, string sectionKey, string outputContract) => new()
    {
        NodeId = id,
        ArtifactKey = sectionKey,
        Role = "analyst",
        InstructionsRef = $"instructions/{sectionKey}.md",
        InputContractType = "ScopeRequest",
        OutputContractType = outputContract,
        ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "analyst", BaselineComponents = ["firm-standards"], DynamicFields = [] },
    };

    /// <summary>Walk harness whose agents emit small JSON payloads and whose tool activity records its inputs and returns a fixed result.</summary>
    private sealed class McpHarness
    {
        private readonly WorkflowDefinition _definition;

        public McpHarness(WorkflowDefinition definition)
        {
            _definition = definition;
            Context = new FakeTaskOrchestrationContext();
            Context.ActivityHandlers[WorkflowActivityNames.AgentTaskActivity] = input =>
            {
                var activityInput = (AgentTaskActivityInput)input!;
                AgentUpstreamPayloads[activityInput.NodeId] = activityInput.UpstreamPayload;
                const string payload = """{"schema_version":"1.0"}""";
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
            Context.ActivityHandlers[WorkflowActivityNames.InvokeMcpToolActivity] = input =>
            {
                var activityInput = (McpToolActivityInput)input!;
                ToolInputs.Add(activityInput);
                const string payload = """{"updated":true}""";
                return new McpToolActivityResult
                {
                    NodeId = activityInput.NodeId,
                    ArtifactKey = activityInput.ArtifactKey,
                    ToolRef = activityInput.ToolRef,
                    OutputPayload = payload,
                    OutputHash = CanonicalProfile.Hash(payload),
                    Simulated = false,
                    HostBuild = "test-build",
                };
            };
            Context.ActivityHandlers[WorkflowActivityNames.SnapshotStateActivity] = input =>
            {
                var snapshot = (ExecutionSnapshot)input!;
                return new SnapshotActivityResponse { SnapshotId = $"{snapshot.ExecutionId}:{snapshot.Sequence:D6}" };
            };
            Context.ActivityHandlers[WorkflowActivityNames.ArtifactStateActivity] = input =>
            {
                var request = (ArtifactStateActivityRequest)input!;
                return new ArtifactStateActivityResponse { SectionRef = $"{request.ExecutionId}:{request.ArtifactKey}:v{request.Version}" };
            };
        }

        public FakeTaskOrchestrationContext Context { get; }
        public FakeResiliencePolicyProvider PolicyProvider { get; } = new();
        public List<McpToolActivityInput> ToolInputs { get; } = [];
        public Dictionary<string, string?> AgentUpstreamPayloads { get; } = new(StringComparer.Ordinal);

        public Task<GraphExecutionState> RunWalkAsync() =>
            GraphOrchestratorSteps.RunInitialWalkAsync(Context, OrchestrationFixtures.Input(_definition), new RollbackPlanner(), PolicyProvider, OrchestrationFixtures.WriteClassifier);
    }
}
