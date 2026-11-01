using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.140 / ADR-PA43 — a run pins the contract validator's budgets at its start. The field is
/// additive (ADR-E15): an input recorded without it replays to byte-identical activity inputs, and a
/// pinned record reaches every activity input that validates, every dispatcher child and every
/// generation after <c>ContinueAsNew</c>.
/// </summary>
public sealed class ContractBudgetsPinningTests
{
    private static readonly ContractBudgets Pinned = new() { MaxRuleCost = 500_000, MaxRuleIterations = 50_000, MaxPayloadDepth = 32, MaxViolations = 10 };

    private const string BudgetsJson = """{"schema_version":"1.0","max_rule_cost":500000,"max_rule_iterations":50000,"max_payload_depth":32,"max_violations":10}""";

    private readonly GraphOrchestrator orchestrator = new(new FakeResiliencePolicyProvider(), new RollbackPlanner(), OrchestrationFixtures.WriteClassifier);

    // ─── Replay: a recorded input without the field ──────────────────────────

    [Fact]
    public async Task RunAsync_ARecordedInputWithoutBudgets_ReplaysToByteIdenticalActivityInputs_ThatNameNone()
    {
        var recorded = CanonicalProfile.SerializeCanonical(OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()));
        var original = new Harness();
        var replay = new Harness();

        await orchestrator.RunAsync(original.Context, JsonSerializer.Deserialize<GraphOrchestratorInput>(recorded, CanonicalProfile.Options)!);
        await orchestrator.RunAsync(replay.Context, JsonSerializer.Deserialize<GraphOrchestratorInput>(recorded, CanonicalProfile.Options)!);

        Assert.Equal(original.Actions, replay.Actions);
        Assert.Equal(original.AgentInputBytes, replay.AgentInputBytes);
        Assert.Equal(3, original.AgentInputBytes.Count);
        Assert.All(original.AgentInputBytes, json => Assert.DoesNotContain("contract_budgets", json, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WithBudgets_EveryAgentInputCarriesThePinnedRecord_AndTheActionsDoNotChange()
    {
        var unpinned = new Harness();
        var pinned = new Harness();

        await orchestrator.RunAsync(unpinned.Context, OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()));
        await orchestrator.RunAsync(pinned.Context, OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()) with { ContractBudgets = Pinned });

        Assert.Equal(unpinned.Actions, pinned.Actions);
        Assert.All(pinned.AgentInputs, agentInput => Assert.Equal(Pinned, agentInput.ContractBudgets));
    }

    // ─── The wire: additive and optional ─────────────────────────────────────

    [Fact]
    public void NewMembers_AreNotRequired()
    {
        Assert.Empty(typeof(GraphOrchestratorInput).GetProperty(nameof(GraphOrchestratorInput.ContractBudgets))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));
        Assert.Empty(typeof(AgentTaskActivityInput).GetProperty(nameof(AgentTaskActivityInput.ContractBudgets))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));
        Assert.Empty(typeof(McpToolActivityInput).GetProperty(nameof(McpToolActivityInput.ContractBudgets))!.GetCustomAttributes(typeof(RequiredMemberAttribute), false));
    }

    [Fact]
    public void GraphOrchestratorInput_RecordedWithoutBudgets_ReadsNullAndReserializesToTheSameBytes()
    {
        var recorded = CanonicalProfile.SerializeCanonical(OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()));

        var rehydrated = JsonSerializer.Deserialize<GraphOrchestratorInput>(recorded, CanonicalProfile.Options)!;

        Assert.Null(rehydrated.ContractBudgets);
        Assert.DoesNotContain("contract_budgets", Encoding.UTF8.GetString(recorded), StringComparison.Ordinal);
        Assert.Equal(recorded, CanonicalProfile.SerializeCanonical(rehydrated));
    }

    [Fact]
    public void GraphOrchestratorInput_WithBudgets_WritesThemLast_AndRoundTrips()
    {
        var input = OrchestrationFixtures.Input(OrchestrationFixtures.ThreeArtifactChain()) with { ContractBudgets = Pinned };

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(input));

        Assert.EndsWith($$""","contract_budgets":{{BudgetsJson}}}""", json, StringComparison.Ordinal);
        Assert.Equal(Pinned, JsonSerializer.Deserialize<GraphOrchestratorInput>(json, CanonicalProfile.Options)!.ContractBudgets);
    }

    [Fact]
    public void AgentTaskActivityInput_WithoutBudgets_KeepsItsPreChangeBytes_AndWithThemWritesThemLast()
    {
        const string preChange = """{"node_id":"n-1","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","correlation_id":"c-1","engagement_id":"eng-1","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"execution_id":"x","tool_refs":[]}""";

        var rehydrated = JsonSerializer.Deserialize<AgentTaskActivityInput>(preChange, CanonicalProfile.Options)!;
        var pinned = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated with { ContractBudgets = Pinned }));

        Assert.Null(rehydrated.ContractBudgets);
        Assert.Equal(preChange, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated)));
        Assert.EndsWith($$""","tool_refs":[],"contract_budgets":{{BudgetsJson}}}""", pinned, StringComparison.Ordinal);
    }

    [Fact]
    public void McpToolActivityInput_WithoutBudgets_KeepsItsPreChangeBytes_AndWithThemWritesThemLast()
    {
        const string preChange = """{"node_id":"t-1","tool_ref":"com.example.crm/tickets/get","timeout_seconds":30,"correlation_id":"c-1","execution_id":"x","engagement_id":"eng-1"}""";

        var rehydrated = JsonSerializer.Deserialize<McpToolActivityInput>(preChange, CanonicalProfile.Options)!;
        var pinned = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated with { ContractBudgets = Pinned }));

        Assert.Null(rehydrated.ContractBudgets);
        Assert.Equal(preChange, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated)));
        Assert.EndsWith($$""","engagement_id":"eng-1","contract_budgets":{{BudgetsJson}}}""", pinned, StringComparison.Ordinal);
    }

    // ─── Dispatcher: children and every generation keep the record ──────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DispatcherInputs_CarryTheBudgetsForward(bool pin)
    {
        var budgets = pin ? Pinned : null;
        var input = DispatcherFixtures.Input() with { ContractBudgets = budgets };

        Assert.Equal(budgets, DispatcherSteps.BuildChildInput(new FakeTaskOrchestrationContext(), input, DispatcherFixtures.Item("wi-1")).ContractBudgets);
        Assert.Equal(budgets, DispatcherSteps.BuildNextGenerationInput(input, resolved: null).ContractBudgets);
    }

    /// <summary>A fake context whose activity handlers record the action sequence and each agent input's canonical bytes.</summary>
    private sealed class Harness
    {
        internal Harness()
        {
            Record(WorkflowActivityNames.AgentTaskActivity, input =>
            {
                var agentInput = (AgentTaskActivityInput)input!;
                AgentInputs.Add(agentInput);
                AgentInputBytes.Add(Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(agentInput)));
                return new AgentTaskActivity(new FakeAgentTaskActivityPipeline()).RunAsync(new FakeTaskActivityContext(), agentInput).GetAwaiter().GetResult();
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

        internal List<string> AgentInputBytes { get; } = [];

        private void Record(string name, Func<object?, object> handler) =>
            Context.ActivityHandlers[name] = input =>
            {
                Actions.Add(name);
                return handler(input);
            };
    }
}
