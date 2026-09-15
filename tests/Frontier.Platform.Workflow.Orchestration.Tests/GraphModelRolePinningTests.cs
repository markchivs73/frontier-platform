using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.102 / ADR-PA29 — executions pin their model-role mappings at start. The flag is a replay
/// gate: without it the orchestrator must schedule exactly the action sequence it always did, and
/// with it the pin is action 0 and every agent input carries the historized pin.
/// </summary>
public sealed class GraphModelRolePinningTests
{
    private readonly GraphOrchestrator orchestrator = new(new FakeResiliencePolicyProvider(), new RollbackPlanner(), OrchestrationFixtures.WriteClassifier);

    /// <summary>The ThreeArtifactChain's activity sequence before S13.102 — what recorded history holds.</summary>
    private static readonly string[] PreChangeSequence =
    [
        WorkflowActivityNames.AgentTaskActivity, WorkflowActivityNames.ArtifactStateActivity, WorkflowActivityNames.SnapshotStateActivity,
        WorkflowActivityNames.AgentTaskActivity, WorkflowActivityNames.ArtifactStateActivity, WorkflowActivityNames.SnapshotStateActivity,
        WorkflowActivityNames.AgentTaskActivity, WorkflowActivityNames.ArtifactStateActivity, WorkflowActivityNames.SnapshotStateActivity,
        WorkflowActivityNames.SnapshotStateActivity, WorkflowActivityNames.ConsolidateAuditActivity,
    ];

    // ─── (a) The replay gate and the historized pin ─────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task RunAsync_WithoutTheFlag_SchedulesExactlyThePreChangeSequence(bool? pinModelRoles)
    {
        var harness = new Harness();

        await orchestrator.RunAsync(harness.Context, Input(pinModelRoles));

        Assert.Equal(PreChangeSequence, harness.Actions);
        Assert.All(harness.AgentInputs, agentInput => Assert.Null(agentInput.PinnedMapping));
    }

    [Theory]
    [InlineData("eng-1")]
    [InlineData("SANDBOX-eng-1")]
    public async Task RunAsync_WithTheFlag_PinsFirstThenRunsThePreChangeSequence(string engagementId)
    {
        var harness = new Harness();

        await orchestrator.RunAsync(harness.Context, Input(true, engagementId));

        Assert.Equal([WorkflowActivityNames.PinMappingsActivity, .. PreChangeSequence], harness.Actions);
        Assert.Equal(engagementId, harness.PinRequest!.EngagementId);
        Assert.Equal(["analyst", "deep-reasoning"], harness.PinRequest.RoleIds);
    }

    [Fact]
    public async Task RunAsync_WithTheFlag_EveryAgentInputCarriesTheHistorizedPinAfterTheCurrentPointerMoves()
    {
        var harness = new Harness();
        harness.Context.ExternalEvents[GraphOrchestratorSteps.ArtifactUpdatedEventName] = "scope";
        harness.Context.ActivityHandlers[WorkflowActivityNames.EvaluateCascadeActivity] = _ => new CascadeActivityResponse
        {
            ChangedArtifact = "scope",
            DownstreamArtifacts = ["approach", "pricing"],
            SkippedArtifacts = [],
        };

        await orchestrator.RunAsync(harness.Context, Input(true));

        Assert.Equal(5, harness.AgentInputs.Count);
        Assert.True(harness.CurrentVersion > Harness.StartVersion);
        Assert.All(harness.AgentInputs, agentInput =>
        {
            Assert.Equal(agentInput.Role, agentInput.PinnedMapping!.RoleId);
            Assert.Equal(Harness.StartVersion, agentInput.PinnedMapping.MappingVersion);
        });
    }

    [Fact]
    public void RolesUsed_CollectsAgentRolesOnlyDedupedAndOrdinallySorted()
    {
        var chain = OrchestrationFixtures.ChainWithBusinessGate();
        var definition = chain with { Nodes = [.. chain.Nodes.Select((node, i) => node is AgentTaskNode agent ? agent with { Role = i == 0 ? "zeta" : "Alpha" } : node)] };

        Assert.Equal(["Alpha", "zeta"], GraphOrchestratorSteps.RolesUsed(definition));
    }

    [Fact]
    public void BuildActivityInput_PinsHeldButNoneForTheRole_LeavesThePinNull()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var state = new GraphExecutionState
        {
            StartedAtUtc = OrchestrationFixtures.StartedAtUtc,
            PinnedMappings = new Dictionary<string, ModelRolePin>(StringComparer.Ordinal) { ["other"] = Pin("other", 1) },
        };

        var activityInput = GraphOrchestratorSteps.BuildActivityInput(OrchestrationFixtures.Input(definition), (AgentTaskNode)definition.Nodes[0], "c-1", "x", state);

        Assert.Null(activityInput.PinnedMapping);
    }

    [Fact]
    public void BuildResolutionRequest_CarriesTheNodesPin()
    {
        var definition = OrchestrationFixtures.ThreeArtifactChain();
        var pin = Pin("analyst", 3);
        var state = new GraphExecutionState
        {
            StartedAtUtc = OrchestrationFixtures.StartedAtUtc,
            PinnedMappings = new Dictionary<string, ModelRolePin>(StringComparer.Ordinal) { ["analyst"] = pin },
        };
        var activityInput = GraphOrchestratorSteps.BuildActivityInput(OrchestrationFixtures.Input(definition), (AgentTaskNode)definition.Nodes[0], "c-1", "x", state);

        var request = AgentTaskActivityPipeline.BuildResolutionRequest(activityInput);

        Assert.Same(pin, request.Pin);
        Assert.Null(request.MappingVersion);
    }

    // ─── The activity ────────────────────────────────────────────────────────

    [Fact]
    public async Task PinMappingsActivity_DelegatesToThePinner()
    {
        var pinner = new FakePinner(() => [Pin("analyst", 2)]);
        var request = new PinMappingsRequest { EngagementId = "eng-1", RoleIds = ["analyst"] };

        var pins = await new PinMappingsActivity(pinner).RunAsync(new FakeTaskActivityContext(), request);

        Assert.Equal([Pin("analyst", 2)], pins);
        Assert.Equal("eng-1", pinner.ReceivedEngagementId);
        Assert.Equal(["analyst"], pinner.ReceivedRoleIds!);
    }

    [Fact]
    public async Task PinMappingsActivity_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new PinMappingsActivity(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new PinMappingsActivity(new FakePinner(() => [])).RunAsync(new FakeTaskActivityContext(), null!));
    }

    // ─── (c) ADR-E15 floor ───────────────────────────────────────────────────

    [Fact]
    public void NewMembers_AreNotRequired()
    {
        Assert.Empty(typeof(GraphOrchestratorInput).GetProperty(nameof(GraphOrchestratorInput.PinModelRoles))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));
        Assert.Empty(typeof(AgentTaskActivityInput).GetProperty(nameof(AgentTaskActivityInput.PinnedMapping))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));
    }

    [Fact]
    public void GraphOrchestratorInput_RecordedWithoutTheFlag_ReadsNullAndReserializesToTheSameBytes()
    {
        var recorded = CanonicalProfile.SerializeCanonical(OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()));

        var rehydrated = JsonSerializer.Deserialize<GraphOrchestratorInput>(recorded, CanonicalProfile.Options)!;

        Assert.Null(rehydrated.PinModelRoles);
        Assert.DoesNotContain("pin_model_roles", Encoding.UTF8.GetString(recorded), StringComparison.Ordinal);
        Assert.Equal(recorded, CanonicalProfile.SerializeCanonical(rehydrated));
    }

    [Fact]
    public void AgentTaskActivityInput_WithoutAPin_KeepsItsPreChangeBytes()
    {
        const string preChange = """{"node_id":"n-1","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","correlation_id":"c-1","engagement_id":"eng-1","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"execution_id":"x","tool_refs":[]}""";

        var rehydrated = JsonSerializer.Deserialize<AgentTaskActivityInput>(preChange, CanonicalProfile.Options)!;

        Assert.Null(rehydrated.PinnedMapping);
        Assert.Equal(preChange, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated)));
    }

    [Fact]
    public void AgentTaskActivityInput_WithAPin_WritesItLast()
    {
        const string preChange = """{"node_id":"n-1","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","correlation_id":"c-1","engagement_id":"eng-1","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"execution_id":"x","tool_refs":[]}""";
        var pinned = JsonSerializer.Deserialize<AgentTaskActivityInput>(preChange, CanonicalProfile.Options)! with { PinnedMapping = Pin("analyst", 2) };

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(pinned));

        Assert.EndsWith(""","tool_refs":[],"pinned_mapping":{"role_id":"analyst","mapping_version":2,"ring":"fleet"}}""", json, StringComparison.Ordinal);
    }

    // ─── (d) Dispatcher: each child pins at its own start ───────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void DispatcherInputs_CarryTheFlagForward(bool? pinModelRoles)
    {
        var input = DispatcherFixtures.Input() with { PinModelRoles = pinModelRoles };

        Assert.Equal(pinModelRoles, DispatcherSteps.BuildChildInput(new FakeTaskOrchestrationContext(), input, DispatcherFixtures.Item("wi-1")).PinModelRoles);
        Assert.Equal(pinModelRoles, DispatcherSteps.BuildNextGenerationInput(input, resolved: null).PinModelRoles);
    }

    private static GraphOrchestratorInput Input(bool? pinModelRoles, string engagementId = "eng-1")
    {
        var chain = OrchestrationFixtures.ThreeArtifactChain();
        var definition = chain with { Nodes = [chain.Nodes[0], ((AgentTaskNode)chain.Nodes[1]) with { Role = "deep-reasoning" }, chain.Nodes[2]] };
        return OrchestrationFixtures.Input(definition, engagementId) with { PinModelRoles = pinModelRoles };
    }

    private static ModelRolePin Pin(string roleId, int version) => new() { RoleId = roleId, MappingVersion = version, Ring = RolloutRing.Fleet };

    /// <summary>A fake context whose activity handlers record the action sequence; every agent call moves the fake store's current pointer.</summary>
    private sealed class Harness
    {
        internal const int StartVersion = 3;

        internal Harness()
        {
            var pinner = new FakePinner(() => [Pin("analyst", CurrentVersion), Pin("deep-reasoning", CurrentVersion)]);
            Record(WorkflowActivityNames.PinMappingsActivity, input =>
            {
                PinRequest = (PinMappingsRequest)input!;
                return new PinMappingsActivity(pinner).RunAsync(new FakeTaskActivityContext(), PinRequest).GetAwaiter().GetResult();
            });
            Record(WorkflowActivityNames.AgentTaskActivity, input =>
            {
                AgentInputs.Add((AgentTaskActivityInput)input!);
                CurrentVersion++;
                return new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), (AgentTaskActivityInput)input!).GetAwaiter().GetResult();
            });
            Record(WorkflowActivityNames.ArtifactStateActivity, input =>
                new ArtifactStateActivityResponse { SectionRef = $"{((ArtifactStateActivityRequest)input!).ArtifactKey}:v{((ArtifactStateActivityRequest)input!).Version}" });
            Record(WorkflowActivityNames.SnapshotStateActivity, input =>
                new SnapshotActivityResponse { SnapshotId = $"s:{((ExecutionSnapshot)input!).Sequence}" });
            Record(WorkflowActivityNames.ConsolidateAuditActivity, input => AuditFixtures.SignedRecord((ConsolidateAuditInput)input!));
        }

        internal FakeTaskOrchestrationContext Context { get; } = new();

        internal List<string> Actions { get; } = [];

        internal List<AgentTaskActivityInput> AgentInputs { get; } = [];

        internal PinMappingsRequest? PinRequest { get; private set; }

        internal int CurrentVersion { get; private set; } = StartVersion;

        private void Record(string name, Func<object?, object> handler) =>
            Context.ActivityHandlers[name] = input =>
            {
                Actions.Add(name);
                return handler(input);
            };
    }

    private sealed class FakePinner(Func<IReadOnlyList<ModelRolePin>> pins) : IMappingPinner
    {
        internal string? ReceivedEngagementId { get; private set; }

        internal IReadOnlyList<string>? ReceivedRoleIds { get; private set; }

        public Task<IReadOnlyList<ModelRolePin>> PinAsync(string engagementId, IEnumerable<string> roleIds, CancellationToken cancellationToken)
        {
            ReceivedEngagementId = engagementId;
            ReceivedRoleIds = [.. roleIds];
            return Task.FromResult(pins());
        }
    }
}
