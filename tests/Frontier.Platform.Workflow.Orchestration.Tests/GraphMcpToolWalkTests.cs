using System.Text.Json;
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

    /// <summary>ADR-E27 D15: an MCP node naming registry contracts gets their pinned snapshots, and its step records the ref and the hashes.</summary>
    [Fact]
    public async Task ToolNodeWithRegistryContracts_PinsThemAndRecordsTheirHashes()
    {
        var definition = Definition(toolRef: "com.example.crm/tickets/get_new_ticket");
        definition = definition with
        {
            Nodes = [.. definition.Nodes.Select(n => n is McpToolNode tool ? tool with { OutputContractType = "acme/ticket/1.0" } : n)],
            Contracts = [RegistryContractFixtures.Ticket],
        };
        var harness = new McpHarness(definition);

        var state = await harness.RunWalkAsync();

        Assert.Same(RegistryContractFixtures.Ticket, Assert.Single(harness.ToolInputs).OutputContract);
        var step = Assert.Single(state.CompletedSteps, s => s.NodeId == "t-tool");
        Assert.Equal("acme/ticket/1.0", step.OutputContractType);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, step.OutputContractHash);
        Assert.Null(step.InputContractHash);
    }

    /// <summary>ADR-PA41: an MCP step names its tool, its input contract and the arguments' hash, so the audit can record it.</summary>
    [Fact]
    public async Task ToolNode_StepNamesItsToolInputContractAndArgumentsHash()
    {
        var definition = Definition(toolRef: "com.example.crm/tickets/update_ticket");
        definition = definition with
        {
            Nodes = [.. definition.Nodes.Select(n => n is McpToolNode tool ? tool with { InputContractType = "acme/ticket-update/1.0" } : n)],
        };
        var harness = new McpHarness(definition);

        var state = await harness.RunWalkAsync();

        var step = Assert.Single(state.CompletedSteps, s => s.NodeId == "t-tool");
        Assert.Equal("com.example.crm/tickets/update_ticket", step.ToolRef);
        Assert.Equal("acme/ticket-update/1.0", step.InputContractType);
        Assert.Equal(McpHarness.ArgumentsHash, step.InputHash);
        Assert.Null(step.Simulated); // a real call is never marked
        Assert.Null(Assert.Single(state.CompletedSteps, s => s.NodeId != "t-tool").ToolRef); // only MCP steps name a tool
    }

    /// <summary>A write-fenced call is marked simulated on its step, as on an agent's <c>ToolCall</c>.</summary>
    [Fact]
    public async Task ToolNode_SimulatedCall_IsMarkedOnTheStep()
    {
        var harness = new McpHarness(Definition(toolRef: "com.example.crm/tickets/update_ticket")) { Simulated = true };

        var state = await harness.RunWalkAsync();

        Assert.True(Assert.Single(state.CompletedSteps, s => s.NodeId == "t-tool").Simulated);
    }

    [Fact]
    public async Task ReadToolNode_NoIdempotencyKey_ReadProfile()
    {
        var harness = new McpHarness(Definition(toolRef: "com.example.crm/tickets/get_new_ticket"));

        await harness.RunWalkAsync();

        Assert.Null(Assert.Single(harness.ToolInputs).IdempotencyKey);
        Assert.Contains(GraphOrchestratorSteps.McpReadProfile, harness.PolicyProvider.RequestedProfileNames);
    }

    /// <summary>ADR-PA41: a pinned <c>writes: true</c> wins over a classifier that calls the tool a read.</summary>
    [Fact]
    public async Task PinnedWrite_OverridesTheClassifier_KeyAndWriteProfile()
    {
        var harness = new McpHarness(WithWrites(Definition(toolRef: "com.example.crm/tickets/get_new_ticket"), writes: true));

        await harness.RunWalkAsync();

        var activityInput = Assert.Single(harness.ToolInputs);
        Assert.Equal(activityInput.CorrelationId, activityInput.IdempotencyKey);
        Assert.Contains(GraphOrchestratorSteps.McpWriteProfile, harness.PolicyProvider.RequestedProfileNames);
        Assert.DoesNotContain(GraphOrchestratorSteps.McpReadProfile, harness.PolicyProvider.RequestedProfileNames);
    }

    /// <summary>ADR-PA41: a pinned <c>writes: false</c> wins over a classifier that calls the tool a write.</summary>
    [Fact]
    public async Task PinnedRead_OverridesTheClassifier_NoKeyAndReadProfile()
    {
        var harness = new McpHarness(WithWrites(Definition(toolRef: "com.example.crm/tickets/update_ticket"), writes: false));

        await harness.RunWalkAsync();

        Assert.Null(Assert.Single(harness.ToolInputs).IdempotencyKey);
        Assert.Contains(GraphOrchestratorSteps.McpReadProfile, harness.PolicyProvider.RequestedProfileNames);
        Assert.DoesNotContain(GraphOrchestratorSteps.McpWriteProfile, harness.PolicyProvider.RequestedProfileNames);
    }

    /// <summary>ADR-PA41: with the flag pinned the classifier is never asked, so its answer cannot vary a replay.</summary>
    [Fact]
    public async Task PinnedFlag_DoesNotConsultTheClassifier()
    {
        var harness = new McpHarness(WithWrites(Definition(toolRef: "com.example.crm/tickets/update_ticket"), writes: true), new ThrowingWriteClassifier());

        await harness.RunWalkAsync();

        Assert.NotNull(Assert.Single(harness.ToolInputs).IdempotencyKey);
    }

    /// <summary>
    /// ADR-PA41 replay: the definition rides inline in the recorded orchestration input (ADR-2), so
    /// a run is replayed from these bytes. Recorded without <c>writes</c>, the step is classified by
    /// the classifier exactly as before; recorded with it, by the pinned value. Each replay of the
    /// same bytes makes the same decision.
    /// </summary>
    [Theory]
    [InlineData("com.example.crm/tickets/update_ticket", "", true)]
    [InlineData("com.example.crm/tickets/get_new_ticket", "", false)]
    [InlineData("com.example.crm/tickets/get_new_ticket", ""","writes":true""", true)]
    [InlineData("com.example.crm/tickets/update_ticket", ""","writes":false""", false)]
    public async Task RecordedDefinition_ReplaysItsWriteClassification(string toolRef, string writesMember, bool expectWrite)
    {
        var recorded = $$"""
            {"schema_version":"2.0","workflow_id":"wf-recorded","definition_version":1,"engagement_type":"support-triage","name":"Recorded run",
             "nodes":[{"node_type":"mcp_tool","node_id":"t-tool","tool_ref":"{{toolRef}}","timeout_seconds":45,"idempotency_key_spec":"correlation"{{writesMember}}}],
             "edges":[],"definition_hash":"sha256:recorded","mode":"one_shot"}
            """;

        var decisions = new List<(string? Key, bool WriteProfile)>();
        for (var replay = 0; replay < 2; replay++)
        {
            var harness = new McpHarness(JsonSerializer.Deserialize<WorkflowDefinition>(recorded, CanonicalProfile.Options)!);
            await harness.RunWalkAsync();
            var activityInput = Assert.Single(harness.ToolInputs);
            decisions.Add((activityInput.IdempotencyKey, harness.PolicyProvider.RequestedProfileNames.Contains(GraphOrchestratorSteps.McpWriteProfile)));
        }

        Assert.Equal(decisions[0], decisions[1]);
        Assert.Equal(expectWrite, decisions[0].Key is not null);
        Assert.Equal(expectWrite, decisions[0].WriteProfile);
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

    private static WorkflowDefinition WithWrites(WorkflowDefinition definition, bool writes) => definition with
    {
        Nodes = [.. definition.Nodes.Select(n => n is McpToolNode tool ? tool with { Writes = writes } : n)],
    };

    /// <summary>A classifier that fails the walk if it is asked at all.</summary>
    private sealed class ThrowingWriteClassifier : IMcpWriteClassifier
    {
        public bool IsWrite(McpToolRef toolRef) => throw new InvalidOperationException($"classifier consulted for '{toolRef}'");
    }

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
        private readonly IMcpWriteClassifier _classifier;

        public McpHarness(WorkflowDefinition definition, IMcpWriteClassifier? classifier = null)
        {
            _definition = definition;
            _classifier = classifier ?? OrchestrationFixtures.WriteClassifier;
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
                    Simulated = Simulated,
                    HostBuild = "test-build",
                    InputHash = ArgumentsHash,
                    InputContractHash = activityInput.InputContract?.Hash,
                    OutputContractHash = activityInput.OutputContract?.Hash,
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

        public const string ArgumentsHash = "args-hash";

        public bool Simulated { get; init; }

        public FakeTaskOrchestrationContext Context { get; }
        public FakeResiliencePolicyProvider PolicyProvider { get; } = new();
        public List<McpToolActivityInput> ToolInputs { get; } = [];
        public Dictionary<string, string?> AgentUpstreamPayloads { get; } = new(StringComparer.Ordinal);

        public Task<GraphExecutionState> RunWalkAsync() =>
            GraphOrchestratorSteps.RunInitialWalkAsync(Context, OrchestrationFixtures.Input(_definition), new RollbackPlanner(), PolicyProvider, _classifier);
    }
}
