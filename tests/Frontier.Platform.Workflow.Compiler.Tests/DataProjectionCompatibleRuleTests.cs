using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-E27 D14: a projection must build an input the consumer's contract accepts.</summary>
public sealed class DataProjectionCompatibleRuleTests
{
    private static readonly IContractValidator Validator = new ServiceCollection()
        .AddFrontierContracts(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<IContractValidator>();

    private const string TicketSchema = """{"type":"object","properties":{"id":{"type":"string"},"note":{"type":"string"},"n":{"type":"integer"}},"required":["id"],"additionalProperties":false}""";
    private const string TriageSchema = """{"type":"object","properties":{"ticket_id":{"type":"string","description":"The ticket."},"channel":{"enum":["email","chat"]},"count":{"type":"integer"}},"required":["ticket_id","channel"],"additionalProperties":false}""";

    private readonly Mock<IContractSnapshotCatalog> _catalog = new();

    public DataProjectionCompatibleRuleTests()
    {
        Register("crm/ticket/1.0", TicketSchema);
        Register("acme/triage/1.0", TriageSchema);
    }

    [Fact]
    public async Task AFittingProjection_HasNoFindings()
    {
        var findings = await Evaluate(
            From("ticket_id", "/id"),
            Literal("channel", "\"email\""),
            From("count", "/n"));

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EveryMisfit_IsNamed()
    {
        var findings = await Evaluate(
            From("ticket_id", "/note"),
            Literal("channel", "\"fax\""),
            From("count", "/id"),
            From("extra", "/id"),
            From("ticket_id", "/missing"));

        Assert.Equal(
            [
                "'ticket_id' is required, but '/note' is not always present in the producer's output.",
                "the literal for 'channel' does not satisfy the consumer's 'channel'.",
                "'count' reads '/id', whose shape differs from the consumer's 'count'.",
                "'extra' is not a property of the consumer's input.",
                "'ticket_id' reads '/missing', which the producer's output does not declare.",
            ],
            findings.Select(f => f.Message));
        Assert.All(findings, f => Assert.Equal(("data.projection-compatible", "get->triage", "projection"), (f.RuleId, f.EdgeRef!, f.FieldPath!)));
    }

    [Fact]
    public async Task ARequiredInputLeftUnprojected_IsNamed()
    {
        var finding = Assert.Single(await Evaluate(From("ticket_id", "/id")));

        Assert.Equal("the consumer requires 'channel', which the projection does not set.", finding.Message);
    }

    [Fact]
    public async Task AProjectionFromAGate_ReadsTheGateDecisionContract()
    {
        Register("acme/approval/1.0", """{"type":"object","properties":{"who":{"type":"string","minLength":1}},"required":["who"],"additionalProperties":false}""");
        var definition = Definition(
            [Gate("get"), Agent("triage", "acme/approval/1.0")],
            [Edge(GateDecision.ContractRef, [From("who", "/approver_id")])]);

        Assert.Empty(await Rule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
    }

    [Fact]
    public async Task EndsThatAreNotRegistryContracts_AreRefusedOnce()
    {
        var clr = Definition([Mcp("get", "crm/ticket/1.0"), Agent("triage", "ScopeRequest")], [Edge("crm/ticket/1.0", [From("ticket_id", "/id")])]);
        var unknown = Definition([Mcp("get", "crm/unknown/1.0"), Agent("triage", "acme/triage/1.0")], [Edge("crm/unknown/1.0", [From("ticket_id", "/id")])]);
        var decision = Definition([new CascadeCheckNode { NodeId = "get", TriggerArtifactKeys = [] }, Agent("triage", "acme/triage/1.0")], [Edge("x/y/1.0", [From("ticket_id", "/id")])]);

        foreach (var definition in new[] { clr, unknown, decision })
        {
            var finding = Assert.Single(await Rule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
            Assert.StartsWith("a projection needs registered contracts at both ends", finding.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnAgentProducer_IsReadThroughItsOutputContract()
    {
        Register("acme/out/1.0", TicketSchema);
        var definition = Definition(
            [Agent("get", "ScopeRequest"), Agent("triage", "acme/triage/1.0")],
            [Edge("acme/out/1.0", [From("ticket_id", "/id"), Literal("channel", "\"chat\"")]), new WorkflowEdge { FromNodeId = "get", ToNodeId = "triage", Kind = EdgeKind.Control }]);

        Assert.Empty(await Rule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
    }

    [Fact]
    public async Task EdgesWithoutAProjection_AreNotChecked()
    {
        var definition = Definition([Mcp("get", "crm/ticket/1.0"), Agent("triage", "ScopeRequest")], [Edge("crm/ticket/1.0", null)]);

        Assert.Empty(await Rule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None));
    }

    [Fact]
    public void InputRef_OfANodeThatTakesNoContract_IsNull()
    {
        Assert.Null(DataProjectionCompatibleRule.InputRef(Gate("g")));
        Assert.Equal("crm/x/1.0", DataProjectionCompatibleRule.InputRef(Mcp("m", null) with { InputContractType = "crm/x/1.0" }));
    }

    [Fact]
    public async Task GuardsRefuseNull()
    {
        Assert.Throws<ArgumentNullException>(() => new DataProjectionCompatibleRule(null!, Validator));
        Assert.Throws<ArgumentNullException>(() => new DataProjectionCompatibleRule(_catalog.Object, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Rule().EvaluateAsync(null!, CancellationToken.None));
    }

    private Task<IReadOnlyList<ValidationFinding>> Evaluate(params ProjectionField[] fields) =>
        Rule().EvaluateAsync(new DefinitionValidationContext(Definition(
            [Mcp("get", "crm/ticket/1.0"), Agent("triage", "acme/triage/1.0")],
            [Edge("crm/ticket/1.0", fields)])), CancellationToken.None);

    private DataProjectionCompatibleRule Rule() => new(_catalog.Object, Validator);

    private void Register(string contractRef, string schema)
    {
        var element = JsonDocument.Parse(schema).RootElement.Clone();
        _catalog.Setup(c => c.FindAsync(contractRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredContract(new ContractSnapshot { ContractRef = contractRef, Schema = element, Rules = [], Hash = ContractHash.Compute(element, []) }, false));
    }

    private static ProjectionField From(string target, string from) => new() { Target = target, From = from };

    private static ProjectionField Literal(string target, string json) => new() { Target = target, Literal = JsonDocument.Parse(json).RootElement.Clone() };

    private static WorkflowEdge Edge(string contractType, IReadOnlyList<ProjectionField>? projection) => new()
    {
        FromNodeId = "get",
        ToNodeId = "triage",
        Kind = EdgeKind.Data,
        ContractType = contractType,
        Projection = projection,
    };

    private static McpToolNode Mcp(string id, string? output) => new()
    {
        NodeId = id,
        ToolRef = "crm/tickets/get",
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "engagementId",
        OutputContractType = output,
    };

    private static AgentTaskNode Agent(string id, string input) => new()
    {
        NodeId = id,
        Role = "analyst",
        InstructionsRef = "instructions/x.md",
        InputContractType = input,
        OutputContractType = "acme/out/1.0",
        ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "analyst", BaselineComponents = [], DynamicFields = [] },
    };

    private static HumanGateNode Gate(string id) => new()
    {
        NodeId = id,
        GateKind = GateKind.Business,
        ApproverRoles = ["partner"],
        PromptTemplate = "ok?",
        TimeoutMinutes = 0,
    };

    private static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "t",
        Name = "n",
        Nodes = nodes,
        Edges = edges,
        DefinitionHash = string.Empty,
        Mode = ExecutionMode.OneShot,
    };
}
