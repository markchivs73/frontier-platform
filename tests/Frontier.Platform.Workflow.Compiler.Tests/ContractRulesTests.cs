using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;

using Moq;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-E27 slice 2's compiler rules: refs resolve, gates emit their own contract, roles bind contracts, and drifted servers stay out.</summary>
public sealed class ContractRulesTests
{
    [Fact]
    public async Task SchemaRefResolves_FlagsUnregisteredAndRetiredRefs_AndAcceptsRegisteredAndEngineOwnedOnes()
    {
        var catalog = new Mock<IContractSnapshotCatalog>();
        catalog.Setup(c => c.FindAsync("acme/in/1.0", It.IsAny<CancellationToken>())).ReturnsAsync(new RegisteredContract(Snapshot("acme/in/1.0"), false));
        catalog.Setup(c => c.FindAsync("acme/old/1.0", It.IsAny<CancellationToken>())).ReturnsAsync(new RegisteredContract(Snapshot("acme/old/1.0"), true));
        var definition = Definition(
            [Agent("a", "acme/in/1.0", "acme/old/1.0"), Agent("b", "acme/missing/1.0", "ScopeSection"), Gate("g")],
            [new WorkflowEdge { FromNodeId = "g", ToNodeId = "a", Kind = EdgeKind.Data, ContractType = GateDecision.ContractRef }]);

        var findings = await new DataSchemaRefResolvesRule(catalog.Object).EvaluateAsync(Context(definition), CancellationToken.None);

        Assert.Equal(
            [
                "contract 'acme/missing/1.0' is not registered. Register it, or name a contract that is.",
                "contract 'acme/old/1.0' is retired. Name a current version.",
            ],
            findings.Select(f => f.Message));
        Assert.All(findings, f => Assert.Equal("data.schema-ref-resolves", f.RuleId));
        catalog.Verify(c => c.FindAsync(GateDecision.ContractRef, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoEdgeFromGate_RequiresTheGateDecisionContract()
    {
        var definition = Definition(
            [Gate("g"), Agent("a", "acme/in/1.0", "acme/out/1.0"), Agent("b", "acme/in/1.0", "acme/out/1.0")],
            [
                new WorkflowEdge { FromNodeId = "g", ToNodeId = "a", Kind = EdgeKind.Data, ContractType = GateDecision.ContractRef },
                new WorkflowEdge { FromNodeId = "g", ToNodeId = "b", Kind = EdgeKind.Data, ContractType = "acme/in/1.0" },
                new WorkflowEdge { FromNodeId = "g", ToNodeId = "b", Kind = EdgeKind.Control },
                new WorkflowEdge { FromNodeId = "a", ToNodeId = "b", Kind = EdgeKind.Data, ContractType = "acme/out/1.0" },
            ]);

        var finding = Assert.Single(await new DataNoEdgeFromGateRule().EvaluateAsync(Context(definition), CancellationToken.None));

        Assert.Equal("g->b", finding.EdgeRef);
        Assert.Contains(GateDecision.ContractRef, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContractsMatch_HoldsANodeToItsRolesContracts_ByIdAndMajor()
    {
        var roles = new Mock<IAgentRoleCatalog>();
        roles.Setup(r => r.GetAgentRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AgentRoleDescriptor { RoleId = "bound", Description = "d", InputContractRef = "acme/in/1.0", OutputContractRef = "acme/out/2.0" },
            new AgentRoleDescriptor { RoleId = "free", Description = "d" },
        ]);
        var definition = Definition(
        [
            Agent("minor-ok", "acme/in/1.3", "acme/out/2.0", "bound"),
            Agent("wrong", "acme/other/1.0", "acme/out/3.0", "bound"),
            Agent("clr", "ScopeRequest", "acme/out/2.0", "bound"),
            Agent("free", "ScopeRequest", "ScopeSection", "free"),
            Agent("unknown", "ScopeRequest", "ScopeSection", "nope"),
        ], []);

        var findings = await new ModelRoleContractsMatchRule(roles.Object).EvaluateAsync(Context(definition), CancellationToken.None);

        Assert.Equal(
            [("wrong", "input_contract_type"), ("wrong", "output_contract_type"), ("clr", "input_contract_type")],
            findings.Select(f => (f.NodeId!, f.FieldPath!)));
        Assert.Equal("role 'bound' takes contract 'acme/in/1.0' as its input; the node names 'acme/other/1.0'. Name the same contract id and major.", findings[0].Message);
        Assert.Contains("as its output", findings[1].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "anything", false)]
    [InlineData("acme/in/1.0", "acme/in/1.9", false)]
    [InlineData("acme/in/1.0", "acme/in/2.0", true)]
    [InlineData("acme/in/1.0", "acme/out/1.0", true)]
    [InlineData("acme/in/1.0", "ScopeRequest", true)]
    [InlineData("acme/in/1.0", "acme/in/x", true)]
    [InlineData("not a ref/", "acme/in/1.0", true)]
    public void ContractsMatch_Mismatch(string? declared, string named, bool expected)
    {
        Assert.Equal(expected, ModelRoleContractsMatchRule.Mismatch(declared, named));
    }

    [Fact]
    public async Task ServerNotDrifted_FlagsEveryCallToADriftedServersTools()
    {
        var tools = new Mock<IDesignerToolCatalog>();
        tools.Setup(t => t.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Tool("crm/tickets/get", "crm/tickets", drifted: true),
            Tool("crm/tickets/put", "crm/tickets", drifted: true),
            Tool("hr/people/get", "hr/people", drifted: false),
        ]);
        var definition = Definition(
        [
            Mcp("m1", "crm/tickets/get"),
            Mcp("m2", "hr/people/get"),
            Agent("a", "ScopeRequest", "ScopeSection") with { ToolRefs = ["crm/tickets/put", "hr/people/get"] },
        ], []);

        var findings = await new McpServerNotDriftedRule(tools.Object).EvaluateAsync(Context(definition), CancellationToken.None);

        Assert.Equal([("m1", "tool_ref"), ("a", "tool_refs")], findings.Select(f => (f.NodeId!, f.FieldPath!)));
        Assert.Contains("server 'crm/tickets'", findings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolResolves_HoldsAnMcpNodeToItsToolsRegisteredContracts()
    {
        var tools = new Mock<IDesignerToolCatalog>();
        tools.Setup(t => t.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Tool("crm/tickets/get", "crm/tickets") with { InputContractRef = "crm/get-in/1.0", OutputContractRef = "crm/ticket/1.0" },
            Tool("crm/tickets/put", "crm/tickets"),
        ]);
        var definition = Definition(
        [
            Mcp("bound", "crm/tickets/get") with { InputContractType = "crm/get-in/1.0", OutputContractType = "crm/ticket/1.0" },
            Mcp("unbound", "crm/tickets/get"),
            Mcp("invented", "crm/tickets/put") with { OutputContractType = "crm/ticket/1.0" },
        ], []);

        var findings = await new McpToolResolvesRule(tools.Object).EvaluateAsync(Context(definition), CancellationToken.None);

        Assert.Equal(
            [("unbound", "input_contract_type"), ("unbound", "output_contract_type"), ("invented", "output_contract_type")],
            findings.Select(f => (f.NodeId!, f.FieldPath!)));
        Assert.Equal("input_contract_type is '(none)', but the registry holds 'crm/get-in/1.0' for tool 'crm/tickets/get'. Name the tool's registered contract.", findings[0].Message);
        Assert.Contains("is 'crm/ticket/1.0', but the registry holds '(none)'", findings[2].Message, StringComparison.Ordinal);
    }

    /// <summary>ADR-PA41: an mcp_tool node pins exactly the write flag the registry holds; null passes only against null.</summary>
    [Fact]
    public async Task ToolResolves_HoldsAnMcpNodeToItsToolsRegisteredWriteFlag()
    {
        var tools = new Mock<IDesignerToolCatalog>();
        tools.Setup(t => t.GetToolsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Tool("crm/tickets/put", "crm/tickets") with { Writes = true },
            Tool("crm/tickets/get", "crm/tickets") with { Writes = false },
            Tool("crm/tickets/old", "crm/tickets"),
        ]);
        var definition = Definition(
        [
            Mcp("write-pinned", "crm/tickets/put") with { Writes = true },
            Mcp("read-pinned", "crm/tickets/get") with { Writes = false },
            Mcp("none-on-none", "crm/tickets/old"),
            Mcp("write-as-read", "crm/tickets/put") with { Writes = false },
            Mcp("unpinned", "crm/tickets/get"),
            Mcp("pinned-on-none", "crm/tickets/old") with { Writes = true },
        ], []);

        var findings = await new McpToolResolvesRule(tools.Object).EvaluateAsync(Context(definition), CancellationToken.None);

        Assert.Equal(
            [("write-as-read", "writes"), ("unpinned", "writes"), ("pinned-on-none", "writes")],
            findings.Select(f => (f.NodeId!, f.FieldPath!)));
        Assert.Equal("writes is 'false', but the registry holds 'true' for tool 'crm/tickets/put'. Pin the tool's registered write flag.", findings[0].Message);
        Assert.Contains("writes is '(none)', but the registry holds 'false'", findings[1].Message, StringComparison.Ordinal);
        Assert.Contains("writes is 'true', but the registry holds '(none)'", findings[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructors_RefuseNull()
    {
        Assert.Throws<ArgumentNullException>(() => new DataSchemaRefResolvesRule(null!));
        Assert.Throws<ArgumentNullException>(() => new ModelRoleContractsMatchRule(null!));
        Assert.Throws<ArgumentNullException>(() => new McpServerNotDriftedRule(null!));
    }

    [Fact]
    public async Task ResourcedRules_RefuseANullContext()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new DataSchemaRefResolvesRule(Mock.Of<IContractSnapshotCatalog>()).EvaluateAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ModelRoleContractsMatchRule(Mock.Of<IAgentRoleCatalog>()).EvaluateAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new McpServerNotDriftedRule(Mock.Of<IDesignerToolCatalog>()).EvaluateAsync(null!, CancellationToken.None));
    }

    private static DefinitionValidationContext Context(WorkflowDefinition definition) => new(definition);

    private static ContractSnapshot Snapshot(string contractRef) => new()
    {
        ContractRef = contractRef,
        Schema = JsonDocument.Parse("{}").RootElement.Clone(),
        Rules = [],
        Hash = "sha256:00",
    };

    private static DesignerToolDescriptor Tool(string toolRef, string server, bool drifted = false) => new()
    {
        ToolRef = toolRef,
        Server = server,
        Name = toolRef.Split('/')[^1],
        Description = "d",
        ServerDrifted = drifted,
    };

    private static AgentTaskNode Agent(string id, string input, string output, string role = "analyst") => new()
    {
        NodeId = id,
        Role = role,
        InstructionsRef = "instructions/x.md",
        InputContractType = input,
        OutputContractType = output,
        ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = role, BaselineComponents = [], DynamicFields = [] },
    };

    private static McpToolNode Mcp(string id, string toolRef) => new()
    {
        NodeId = id,
        ToolRef = toolRef,
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "engagementId",
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
