using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// ADR-PA42: an agent step's tool pins ride on <see cref="AgentTaskActivityInput"/> as an optional
/// <c>tool_pins</c>, omitted when null, so every input recorded before the pins keeps its bytes, and a
/// recorded definition hands its activity the same pins on every replay.
/// </summary>
public sealed class AgentToolPinWireTests
{
    private const string PreChange = """{"node_id":"n-1","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","correlation_id":"c-1","engagement_id":"eng-1","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"execution_id":"x","tool_refs":["com.example.crm/tickets/get_ticket"]}""";

    private const string RecordedNode = """{"node_type":"agent_task","node_id":"triage","role":"analyst","instructions_ref":"i.md","input_contract_type":"In","output_contract_type":"Out","context_request":{"schema_version":"1.0","engagement_id":"eng-1","agent_role":"analyst","baseline_components":[],"dynamic_fields":[],"requires_real_time":false,"real_time_sources":[]},"tool_refs":["com.example.crm/tickets/get_ticket"]""";

    private const string RecordedPins = ""","tool_contracts":[{"tool_ref":"com.example.crm/tickets/get_ticket","input_contract_type":"acme/ticket/1.0","writes":false}]""";

    [Fact]
    public void ToolPins_IsOptional()
    {
        Assert.Empty(typeof(AgentTaskActivityInput).GetProperty(nameof(AgentTaskActivityInput.ToolPins))!.GetCustomAttributes(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), false));
    }

    [Fact]
    public void AgentTaskActivityInput_RecordedWithoutPins_ReadsNullAndKeepsItsBytes()
    {
        var rehydrated = JsonSerializer.Deserialize<AgentTaskActivityInput>(PreChange, CanonicalProfile.Options)!;

        Assert.Null(rehydrated.ToolPins);
        Assert.Equal(PreChange, Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(rehydrated)));
    }

    [Fact]
    public void AgentTaskActivityInput_WithPins_WritesThemLast()
    {
        var pinned = JsonSerializer.Deserialize<AgentTaskActivityInput>(PreChange, CanonicalProfile.Options)! with
        {
            ToolPins = [new McpToolPin { ToolRef = "com.example.crm/tickets/get_ticket", Writes = true }],
        };

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(pinned));

        Assert.EndsWith(""","tool_refs":["com.example.crm/tickets/get_ticket"],"tool_pins":[{"tool_ref":"com.example.crm/tickets/get_ticket","writes":true}]}""", json, StringComparison.Ordinal);
        Assert.Equal(pinned.ToolPins, JsonSerializer.Deserialize<AgentTaskActivityInput>(json, CanonicalProfile.Options)!.ToolPins);
    }

    /// <summary>Replay: the definition rides inline in the recorded input (ADR-2), so the same bytes build the same activity input every time.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedDefinition_BuildsTheSameActivityInputOnEveryReplay(bool withPins)
    {
        var recorded = Recorded(withPins);

        var first = Build(recorded);
        var second = Build(recorded);

        Assert.Equal(first, second);
        Assert.Equal(withPins, first.Contains("\"tool_pins\":", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordedDefinition_WithPins_HandsTheEmbeddedSnapshotAndFlagToTheActivity()
    {
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(Recorded(withPins: true), CanonicalProfile.Options)!;

        var pin = Assert.Single(BuildInput(definition).ToolPins!);

        Assert.Equal(RegistryContractFixtures.Ticket.Hash, pin.InputContract!.Hash);
        Assert.False(pin.Writes);
    }

    private static string Recorded(bool withPins)
    {
        var contracts = JsonSerializer.Serialize(new[] { RegistryContractFixtures.Ticket }, CanonicalProfile.Options);
        return $$"""
            {"schema_version":"2.0","workflow_id":"wf-recorded","definition_version":1,"engagement_type":"support-triage","name":"Recorded run",
             "nodes":[{{RecordedNode}}{{(withPins ? RecordedPins : string.Empty)}}}],
             "edges":[],"definition_hash":"sha256:recorded","mode":"one_shot","contracts":{{contracts}}}
            """;
    }

    private static string Build(string recorded) =>
        Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(BuildInput(JsonSerializer.Deserialize<WorkflowDefinition>(recorded, CanonicalProfile.Options)!)));

    private static AgentTaskActivityInput BuildInput(WorkflowDefinition definition) =>
        GraphOrchestratorSteps.BuildActivityInput(
            OrchestrationFixtures.Input(definition),
            (AgentTaskNode)definition.Nodes[0],
            "c-1",
            "x",
            new GraphExecutionState { StartedAtUtc = OrchestrationFixtures.StartedAtUtc });
}
