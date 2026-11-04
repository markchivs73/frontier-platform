using System.Text.Json;

using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;

using Microsoft.Extensions.AI;

using Moq;

using Xunit;

using static Frontier.Platform.Workflow.Compiler.Tests.McpToolRegistryStampTests;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// ADR-PA41, ADR-PA42: every proposal the design agent returns, the first attempt and the repair alike,
/// has its mcp_tool nodes and its agent nodes' tools stamped from the tool catalogue before it is
/// validated, diffed or recorded on the turn the designer later approves from.
/// </summary>
public sealed class ChatDesignerStampingTests
{
    private const string Ticket = "io.frontier.demo/autotask/get_new_ticket";
    private const string InputRef = "io.frontier.demo.autotask/get-new-ticket-input/1.0";
    private const string OutputRef = "io.frontier.demo.autotask/get-new-ticket-output/1.0";

    [Fact]
    public async Task BothAttempts_AreStampedBeforeValidation_AndTheTurnRecordsTheStampedProposal()
    {
        var compiler = new RecordingCompiler(failFirst: true);
        var (service, store, _) = Build(compiler, Proposal("first", "hand/in/1.0", true), Proposal("second", null, null));

        var turn = await RunTurnAsync(service, store);

        AssertStamped(Assert.Single(compiler.Validated));             // the first attempt, before the repair decision
        AssertStamped(Assert.Single(compiler.StructurallyValidated)); // the repair, before the review's validation
        Assert.Equal("second", turn.ProposalReasoningJson);
        AssertStamped(Deserialize(turn.AgentProposalJson!));
    }

    [Fact]
    public async Task TheDiff_SeesTheStampedValues_SoAnUnchangedToolNodeIsNotReportedModified()
    {
        var (service, store, _) = Build(new RecordingCompiler(failFirst: false), Proposal("only", null, null));

        var turn = await RunTurnAsync(service, store);

        Assert.Empty(turn.ProposalChanges!);
    }

    /// <summary>ADR-PA42: an agent node the design agent proposes has its tools pinned before it is validated.</summary>
    [Fact]
    public async Task AnAgentsTools_ArePinnedBeforeValidation()
    {
        var compiler = new RecordingCompiler(failFirst: false);
        var json = JsonSerializer.Serialize(Definition(Agent([Ticket])), CanonicalProfile.Options);
        var (service, store, _) = Build(compiler, $$"""{"reason":"r","definition":{{json}},"changed_node_ids":["a"]}""");

        await RunTurnAsync(service, store);

        var agent = Assert.IsType<AgentTaskNode>(Assert.Single(Assert.Single(compiler.Validated).Nodes));
        Assert.Equal([new AgentToolContract { ToolRef = Ticket, InputContractType = InputRef, OutputContractType = OutputRef, Writes = true }], agent.ToolContracts);
    }

    [Fact]
    public async Task AProseAnswer_IsNotStamped()
    {
        var (service, store, _) = Build(new RecordingCompiler(failFirst: false), "Which ticket queue do you mean?");

        var turn = await RunTurnAsync(service, store);

        Assert.Equal(string.Empty, turn.AgentProposalJson);
        Assert.Equal("Which ticket queue do you mean?", turn.ProposalReasoningJson);
    }

    private static void AssertStamped(WorkflowDefinition definition)
    {
        var node = Assert.IsType<McpToolNode>(Assert.Single(definition.Nodes));
        Assert.Equal(InputRef, node.InputContractType);
        Assert.Equal(OutputRef, node.OutputContractType);
        Assert.True(node.Writes);
    }

    private static WorkflowDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(json, CanonicalProfile.Options)!;

    private static string Proposal(string reason, string? input, bool? writes)
    {
        var json = JsonSerializer.Serialize(Definition(Mcp("m", Ticket, input, null, writes)), CanonicalProfile.Options);
        return $$"""{"reason":"{{reason}}","definition":{{json}},"changed_node_ids":["m"]}""";
    }

    private static async Task<DesignTurnDocument> RunTurnAsync(ChatDesignerService service, InMemoryDefinitionStore store)
    {
        var draft = new DefinitionDraftDocument
        {
            Id = "wf-x:draft",
            WorkflowId = "wf-x",
            State = "draft",
            BaseVersion = 1,
            Definition = Definition(Mcp("m", Ticket, InputRef, OutputRef, true)),
            DraftRevision = "rev-1",
            LastEditedBy = "user-1",
            LastEditedUtc = DateTime.UtcNow,
        };
        await store.SaveDraftAsync("wf-x", draft, "no-etag", CancellationToken.None);
        return await service.SubmitDesignTurnAsync("wf-x",
            new DesignTurnRequest { DesignerId = "d", Message = "fetch a ticket" }, CancellationToken.None);
    }

    private static (ChatDesignerService Service, InMemoryDefinitionStore Store, Mock<IChatClient> Client) Build(
        IDefinitionCompiler compiler, params string[] responses)
    {
        var store = new InMemoryDefinitionStore();
        var client = new Mock<IChatClient>();
        var seq = client.SetupSequence(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()));
        foreach (var r in responses) seq = seq.Returns(ChatDesignerServiceTests.StreamingResponse(r));
        var service = new ChatDesignerService(store, client.Object,
            new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance),
            new ChatDesignerServiceTests.FakeApproverRoleCatalog(), new StampingToolCatalog(), new ChatDesignerServiceTests.FakeAgentRoleCatalog(),
            new NodeDiffService(), compiler, new ChatDesignerServiceTests.FakeDesignerModelProvider(), new ChatDesignerServiceTests.FakeInstructionCatalog(),
            new ChatDesignerServiceTests.FakeContextComponentCatalog(), new ChatDesignerServiceTests.FakeEntryContractCatalog(), new ChatDesignerServiceTests.FakeContractSnapshotCatalog());
        return (service, store, client);
    }

    /// <summary>A catalogue whose one tool has registered contracts and writes.</summary>
    private sealed class StampingToolCatalog : IDesignerToolCatalog
    {
        public Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DesignerToolDescriptor>>([Tool(Ticket, InputRef, OutputRef, true)]);
    }

    /// <summary>Records every definition either tier is asked to validate; optionally fails the first resourced pass, forcing one repair.</summary>
    private sealed class RecordingCompiler(bool failFirst) : IDefinitionCompiler
    {
        public List<WorkflowDefinition> Validated { get; } = [];

        public Task<ValidationReport> ValidateAsync(WorkflowDefinition definition, string draftRevision, string? storedSchemaVersion, CancellationToken ct)
        {
            Validated.Add(definition);
            IReadOnlyList<ValidationFinding> findings = failFirst && Validated.Count == 1
                ? [new ValidationFinding("data.edge-type-match", ValidationSeverity.Error, "mismatch", NodeId: "m")]
                : [];
            return Task.FromResult(new ValidationReport("wf-x", draftRevision, DateTime.UtcNow,
                findings.Count > 0 ? ValidationOutcome.Fail : ValidationOutcome.Pass, findings, new Dictionary<string, string>()));
        }

        public List<WorkflowDefinition> StructurallyValidated { get; } = [];

        public IReadOnlyList<ValidationFinding> ValidateStructural(WorkflowDefinition definition)
        {
            StructurallyValidated.Add(definition);
            return [];
        }

        public string ComputeDefinitionHash(WorkflowDefinition definition) => "hash";
    }
}
