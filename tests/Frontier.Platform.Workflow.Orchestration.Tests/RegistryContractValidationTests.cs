using System.Text.Json;
using System.Text.Json.Nodes;

using Frontier.Platform.Abstractions;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>ADR-E27 D7, D8, D15: an agent node naming registry contracts is validated against the definition's pinned snapshots.</summary>
public sealed class RegistryContractValidationTests
{
    private const string GoodTicket = """{ "priority": 2, "id": "T-1" }""";

    [Fact]
    public void Ensure_ReturnsThePayloadBytesWhenItSatisfiesTheContract()
    {
        var bytes = RegistryContractCheck.Ensure(TestContractValidator.Instance, RegistryContractFixtures.Ticket, GoodTicket);

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(GoodTicket), bytes);
    }

    [Fact]
    public void Ensure_RefusesAPayloadThatFailsTheSchemaOrARule_NamingTheContract()
    {
        var schema = Assert.Throws<ContractViolationException>(() =>
            RegistryContractCheck.Ensure(TestContractValidator.Instance, RegistryContractFixtures.Ticket, """{"id":"","priority":1}"""));
        var rule = Assert.Throws<ContractViolationException>(() =>
            RegistryContractCheck.Ensure(TestContractValidator.Instance, RegistryContractFixtures.Ticket, """{"id":"T-1","priority":-1}"""));

        Assert.Equal("acme/ticket/1.0", schema.ContractType);
        Assert.StartsWith("/id: minLength: ", Assert.Single(schema.Violations), StringComparison.Ordinal);
        Assert.Contains("Priority is never negative.", Assert.Single(rule.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_NamesTheRootAsASlash()
    {
        Assert.Equal("/: type: m", RegistryContractCheck.Describe(new Contracts.ContractViolation("", "type", "m")));
    }

    [Fact]
    public async Task Dispatcher_StoresAValidRegistryReplyAsReceived_AndHashesItsJcsForm()
    {
        var invoker = new FakeAgentInvoker(null, output: GoodTicket);
        var dispatcher = Dispatcher(invoker);

        var result = await dispatcher.InvokeAsync("acme/ticket/1.0", RegistryContractFixtures.Ticket, Request(ModelTarget()), CancellationToken.None);

        Assert.Equal(GoodTicket, result.Payload);
        Assert.Equal(JsonCanonicalizer.Hash("""{"id":"T-1","priority":2}"""u8), result.Hash);
        Assert.Same(RegistryOutputSchema.For(RegistryContractFixtures.Ticket), invoker.ReceivedRequest!.ResponseFormat);
    }

    [Fact]
    public async Task Dispatcher_RefusesARegistryReplyThatFailsItsContract()
    {
        var dispatcher = Dispatcher(new FakeAgentInvoker(null, output: """{"id":"T-1"}"""));

        await Assert.ThrowsAsync<ContractViolationException>(() =>
            dispatcher.InvokeAsync("acme/ticket/1.0", RegistryContractFixtures.Ticket, Request(ModelTarget()), CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_AsksAnAgentTargetForNoFormat_EvenForARegistryContract()
    {
        var invoker = new FakeAgentInvoker(null, output: GoodTicket);

        await Dispatcher(invoker).InvokeAsync("acme/ticket/1.0", RegistryContractFixtures.Ticket, Request(AgentTarget()), CancellationToken.None);

        Assert.Null(invoker.ReceivedRequest!.ResponseFormat);
    }

    [Fact]
    public void PinnedContract_FindsTheEmbeddedSnapshotByExactRef()
    {
        var definition = Definition([RegistryContractFixtures.Ticket]);

        Assert.Same(RegistryContractFixtures.Ticket, GraphOrchestratorSteps.PinnedContract(definition, "acme/ticket/1.0"));
        Assert.Null(GraphOrchestratorSteps.PinnedContract(definition, "acme/ticket/1.1"));
        Assert.Null(GraphOrchestratorSteps.PinnedContract(definition, "ScopeSection"));
        Assert.Null(GraphOrchestratorSteps.PinnedContract(definition, null));
        Assert.Null(GraphOrchestratorSteps.PinnedContract(Definition(null), "acme/ticket/1.0"));
    }

    [Fact]
    public void Lower_KeepsStructure_DropsNarrowingKeywords()
    {
        var lowered = RegistryOutputSchema.Lower(JsonNode.Parse(RegistryContractFixtures.Ticket.Schema.GetRawText()));

        Assert.Equal(
            """{"type":"object","description":"A ticket.","properties":{"id":{"type":"string","description":"Id."},"priority":{"type":"number","description":"Priority."}},"required":["id","priority"],"additionalProperties":false}""",
            lowered!.ToJsonString());
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}},"required":[],"additionalProperties":false}""")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}},"required":["a"]}""")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}},"required":["a"],"additionalProperties":true}""")]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"type":"string","additionalProperties":false}""")]
    [InlineData("""{"oneOf":[{"type":"string"}]}""")]
    [InlineData("""{"type":"array","items":{"type":"object"}}""")]
    [InlineData("""{"anyOf":[{"type":"object"}]}""")]
    [InlineData("""{"$defs":{"x":{"type":"object"}}}""")]
    [InlineData("""{"properties":[]}""")]
    [InlineData("""{"anyOf":{}}""")]
    [InlineData("""true""")]
    [InlineData("""{"title":null}""")]
    public void Lower_RefusesWhatStrictModesCannotExpress(string schema)
    {
        Assert.Null(RegistryOutputSchema.Lower(JsonNode.Parse(schema)));
    }

    [Fact]
    public void Lower_KeepsAnyOfItemsAndDefs()
    {
        const string schema = """{"$defs":{"s":{"type":"string"}},"type":"array","items":{"anyOf":[{"$ref":"#/$defs/s"},{"type":"object","properties":{},"additionalProperties":false}]}}""";

        Assert.Equal(schema, RegistryOutputSchema.Lower(JsonNode.Parse(schema))!.ToJsonString());
    }

    [Fact]
    public void For_RefusesALoweringFailure_AndCachesByHash()
    {
        var open = RegistryContractFixtures.Snapshot("acme/open/1.0", """{"type":"object","description":"Open.","additionalProperties":true}""");

        Assert.Null(RegistryOutputSchema.For(open));
        Assert.Same(RegistryOutputSchema.For(RegistryContractFixtures.Ticket), RegistryOutputSchema.For(RegistryContractFixtures.Ticket));
        Assert.Throws<ArgumentNullException>(() => RegistryOutputSchema.For(null!));
    }

    [Fact]
    public void SchemaName_KeepsOnlyProviderSafeCharacters()
    {
        Assert.Equal("com_example_crm_ticket-v_1_0", RegistryOutputSchema.SchemaName("com.example.crm/ticket-v/1.0"));
    }

    private static AgentInvocationDispatcher Dispatcher(IAgentInvoker invoker) =>
        new(invoker, new ContractTypeRegistry(TestContractSet.Instance), TestContractValidator.Instance);

    private static AgentInvocationRequest Request(ChainEntry target) => new()
    {
        Instructions = "instructions",
        Prompt = "prompt",
        ModelId = "m",
        Target = target,
        MaxOutputTokens = 100,
    };

    private static ChainEntry ModelTarget() => Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];

    private static AgentEntry AgentTarget() => new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/echo",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    private static WorkflowDefinition Definition(IReadOnlyList<ContractSnapshot>? contracts) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "t",
        Name = "n",
        Nodes = [],
        Edges = [],
        DefinitionHash = "sha256:x",
        Mode = ExecutionMode.OneShot,
        Contracts = contracts,
    };
}
