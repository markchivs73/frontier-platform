using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>ADR-E27 D15: which refs a definition names, and how publish pins them.</summary>
public sealed class DefinitionContractsTests
{
    private readonly Mock<IContractSnapshotCatalog> _catalog = new();

    [Fact]
    public void Collect_NamesEverySchemaRefOnNodesAndDataEdges_SortedAndDistinct()
    {
        var definition = Definition(
            [
                Agent("agent", "acme/in/1.0", "acme/out/1.0"),
                Mcp("tool", "crm/args/1.0", null),
                Mcp("tool-2", null, "acme/out/1.0"),
                new HumanGateNode { NodeId = "gate", GateKind = GateKind.Business, ApproverRoles = ["partner"], PromptTemplate = "ok?", TimeoutMinutes = 0 },
            ],
            [
                new WorkflowEdge { FromNodeId = "gate", ToNodeId = "agent", Kind = EdgeKind.Data, ContractType = GateDecision.ContractRef },
                new WorkflowEdge { FromNodeId = "tool", ToNodeId = "agent", Kind = EdgeKind.Data, ContractType = "ScopeSection" },
                new WorkflowEdge { FromNodeId = "tool", ToNodeId = "agent", Kind = EdgeKind.Control, ContractType = "ignored/control/1.0" },
            ]);

        Assert.Equal(
            ["acme/in/1.0", "acme/out/1.0", "crm/args/1.0", GateDecision.ContractRef],
            DefinitionContractRefs.Collect(definition));
    }

    [Fact]
    public void Collect_IgnoresClrContractNames()
    {
        Assert.Empty(DefinitionContractRefs.Collect(Definition([Agent("agent", "ScopeRequest", "ScopeSection")], [])));
    }

    [Fact]
    public void Collect_RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => DefinitionContractRefs.Collect(null!));
    }

    [Fact]
    public async Task EmbedAsync_LeavesADefinitionThatNamesNoRefWithoutContracts()
    {
        var definition = Definition([Agent("agent", "ScopeRequest", "ScopeSection")], []) with { Contracts = [Snapshot("stale/x/1.0")] };

        var embedded = await Embedder().EmbedAsync(definition, CancellationToken.None);

        Assert.Null(embedded.Contracts);
        _catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmbedAsync_PinsRegistryAndEngineContractsSortedByRef()
    {
        Register("acme/out/1.0");
        Register("acme/in/1.0");
        var definition = Definition(
            [Agent("agent", "acme/in/1.0", "acme/out/1.0")],
            [new WorkflowEdge { FromNodeId = "gate", ToNodeId = "agent", Kind = EdgeKind.Data, ContractType = GateDecision.ContractRef }]);

        var embedded = await Embedder().EmbedAsync(definition, CancellationToken.None);

        Assert.Equal(["acme/in/1.0", "acme/out/1.0", GateDecision.ContractRef], embedded.Contracts!.Select(c => c.ContractRef));
        Assert.Same(EngineContracts.GateDecision, embedded.Contracts![2]);
        _catalog.Verify(c => c.FindAsync(GateDecision.ContractRef, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmbedAsync_RefusesUnregisteredRetiredAndMisfiledContracts_NamingEach()
    {
        Register("acme/retired/1.0", retired: true);
        _catalog.Setup(c => c.FindAsync("acme/misfiled/1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredContract(Snapshot("acme/other/1.0"), IsRetired: false));
        Register("acme/fine/1.0");
        var definition = Definition(
            [Agent("a", "acme/missing/1.0", "acme/retired/1.0"), Agent("b", "acme/misfiled/1.0", "acme/fine/1.0")], []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Embedder().EmbedAsync(definition, CancellationToken.None));

        Assert.Equal(
            "Cannot publish: these contracts are not registered, or are retired: acme/misfiled/1.0, acme/missing/1.0, acme/retired/1.0. Register them, or name a current version.",
            ex.Message);
    }

    private DefinitionContractEmbedder Embedder() => new(_catalog.Object);

    private void Register(string contractRef, bool retired = false) =>
        _catalog.Setup(c => c.FindAsync(contractRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisteredContract(Snapshot(contractRef), retired));

    private static ContractSnapshot Snapshot(string contractRef) => new()
    {
        ContractRef = contractRef,
        Schema = JsonDocument.Parse("{}").RootElement.Clone(),
        Rules = [],
        Hash = "sha256:00",
    };

    private static AgentTaskNode Agent(string id, string input, string output) => new()
    {
        NodeId = id,
        Role = "analyst",
        InstructionsRef = "instructions/x.md",
        InputContractType = input,
        OutputContractType = output,
        ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "analyst", BaselineComponents = [], DynamicFields = [] },
    };

    private static McpToolNode Mcp(string id, string? input, string? output) => new()
    {
        NodeId = id,
        ToolRef = "com.example.crm/tickets/get_ticket",
        TimeoutSeconds = 30,
        IdempotencyKeySpec = "engagementId",
        InputContractType = input,
        OutputContractType = output,
    };

    private static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "support-triage",
        Name = "Contracts",
        Nodes = nodes,
        Edges = edges,
        DefinitionHash = string.Empty,
        Mode = ExecutionMode.OneShot,
    };
}

/// <summary>ADR-E27 D15: the compiler registers the embedder over the consumer's catalog.</summary>
public sealed class DefinitionContractEmbedderRegistrationTests
{
    [Fact]
    public void AddFrontierWorkflowCompiler_ResolvesTheEmbedderOverTheRegisteredCatalog()
    {
        var services = new ServiceCollection().AddFrontierWorkflowCompiler();
        services.AddScoped(_ => Mock.Of<IContractSnapshotCatalog>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DefinitionContractEmbedder>(scope.ServiceProvider.GetRequiredService<IDefinitionContractEmbedder>());
    }
}
