using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S9.24: <see cref="ProposalMergeService"/>'s edge-merge path and <c>LoadLatestProposalAsync</c>'s malformed-JSON guard — untouched by the existing granular suite (node-only coverage).</summary>
public sealed class ProposalMergeServiceEdgeMergeTests
{
    private static AgentTaskNode Agent(string id) => new()
    {
        NodeId = id,
        ArtifactKey = "scope",
        Role = "deep-reasoning",
        InstructionsRef = "instructions/scope.md",
        InputContractType = "BriefArtifact",
        OutputContractType = "SummaryArtifact",
        ContextRequest = new ContextRequest { EngagementId = "eng-1", AgentRole = "deep-reasoning", BaselineComponents = [], DynamicFields = [] },
    };

    private static WorkflowDefinition Definition(IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf-1",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "fixture",
        Nodes = nodes,
        Edges = edges,
        DefinitionHash = "hash",
        Mode = ExecutionMode.OneShot,
    };

    [Fact]
    public void ApplyApproved_ApprovedEdgeAdd_IncludesTheEdge()
    {
        var current = Definition([Agent("n1"), Agent("n2")], []);
        var edge = new WorkflowEdge { FromNodeId = "n1", ToNodeId = "n2", Kind = EdgeKind.Control };
        var proposed = Definition([Agent("n1"), Agent("n2")], [edge]);
        var approved = new HashSet<string>(StringComparer.Ordinal) { "edge:added:n1→n2 (control)" };

        var merged = ProposalMergeService.ApplyApproved(current, proposed, approved);

        Assert.Single(merged.Edges);
        Assert.Equal("n1", merged.Edges[0].FromNodeId);
    }

    [Fact]
    public void ApplyApproved_UnapprovedEdgeAdd_IsExcluded()
    {
        var current = Definition([Agent("n1"), Agent("n2")], []);
        var proposed = Definition([Agent("n1"), Agent("n2")], [new WorkflowEdge { FromNodeId = "n1", ToNodeId = "n2", Kind = EdgeKind.Control }]);

        var merged = ProposalMergeService.ApplyApproved(current, proposed, []);

        Assert.Empty(merged.Edges);
    }

    [Fact]
    public void ApplyApproved_ApprovedEdgeRemove_DropsTheEdge()
    {
        var edge = new WorkflowEdge { FromNodeId = "n1", ToNodeId = "n2", Kind = EdgeKind.Control };
        var current = Definition([Agent("n1"), Agent("n2")], [edge]);
        var proposed = Definition([Agent("n1"), Agent("n2")], []);
        var approved = new HashSet<string>(StringComparer.Ordinal) { "edge:removed:n1→n2 (control)" };

        var merged = ProposalMergeService.ApplyApproved(current, proposed, approved);

        Assert.Empty(merged.Edges);
    }

    [Fact]
    public void ApplyApproved_ControlAndDataEdgeBetweenSamePair_KeepsBoth()
    {
        // S9.27 live-walkthrough regression: with a kind-less edge key, the data edge
        // overwrote the control edge in the merge dictionary, silently dropping the whole
        // control spine and failing graph.single-entry-reachable on apply.
        var current = Definition([Agent("n1"), Agent("n2")], []);
        var proposed = Definition([Agent("n1"), Agent("n2")],
        [
            new WorkflowEdge { FromNodeId = "n1", ToNodeId = "n2", Kind = EdgeKind.Control },
            new WorkflowEdge { FromNodeId = "n1", ToNodeId = "n2", Kind = EdgeKind.Data, ContractType = "TicketDetails" },
        ]);
        var approved = new HashSet<string>(StringComparer.Ordinal)
        {
            "edge:added:n1→n2 (control)",
            "edge:added:n1→n2 (data)",
        };

        var merged = ProposalMergeService.ApplyApproved(current, proposed, approved);

        Assert.Equal(2, merged.Edges.Count);
        Assert.Contains(merged.Edges, e => e.Kind == EdgeKind.Control);
        Assert.Contains(merged.Edges, e => e.Kind == EdgeKind.Data);
    }

    [Fact]
    public async Task LoadLatestProposalAsync_MalformedAgentProposalJson_ReturnsNull()
    {
        var store = new Mock<IDefinitionStore>();
        store.Setup(s => s.GetAllDesignTurnsAsync("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DesignTurnDocument
            {
                Id = "wf-1:turn:1",
                WorkflowId = "wf-1",
                DraftId = "wf-1:draft",
                TurnNumber = 1,
                DesignerId = "user-1",
                CreatedAtUtc = DateTime.UtcNow,
                DesignerMessage = "do it",
                DraftRevisionAtTurn = "rev-1",
                AgentProposalJson = "{not valid json",
            }]);
        var service = new ProposalMergeService(store.Object, new Mock<IDefinitionCompiler>().Object, new NodeDiffService());

        var result = await service.LoadLatestProposalAsync("wf-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadLatestProposalAsync_NoTurnsWithProposal_ReturnsNull()
    {
        var store = new Mock<IDefinitionStore>();
        store.Setup(s => s.GetAllDesignTurnsAsync("wf-1", It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new ProposalMergeService(store.Object, new Mock<IDefinitionCompiler>().Object, new NodeDiffService());

        Assert.Null(await service.LoadLatestProposalAsync("wf-1", CancellationToken.None));
    }

    [Fact]
    public async Task ApplyProposalAsync_NonRevPrefixedBaseRevision_GeneratesRandomRevision()
    {
        // GenerateNewRevision's "rev-N" fast path only applies when the current revision
        // already follows that convention; drafts seeded with any other id (e.g. the
        // initial "draft-1" some fixtures use) fall back to a random rev-{guid} token.
        var draft = Definition([Agent("n1")], []);
        var draftDoc = new DefinitionDraftDocument
        {
            Id = "wf-1:draft",
            WorkflowId = "wf-1",
            State = "draft",
            BaseVersion = 1,
            DraftRevision = "initial-import",
            Definition = draft,
            LastEditedBy = "user-1",
            LastEditedUtc = DateTime.UtcNow,
        };
        var store = new Mock<IDefinitionStore>();
        store.Setup(s => s.GetDraftAsync("wf-1", It.IsAny<CancellationToken>())).ReturnsAsync(draftDoc);
        store.Setup(s => s.SaveDraftAsync("wf-1", It.IsAny<DefinitionDraftDocument>(), "no-etag-check", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DefinitionDraftDocument d, string _, CancellationToken _) => new SaveDraftResultSuccess(d));
        var compiler = new Mock<IDefinitionCompiler>();
        compiler.Setup(c => c.ValidateStructural(It.IsAny<WorkflowDefinition>())).Returns([]);
        var service = new ProposalMergeService(store.Object, compiler.Object, new NodeDiffService());
        var proposedJson = System.Text.Json.JsonSerializer.Serialize(draft, Frontier.Platform.Serialization.CanonicalProfile.Options);

        var result = await service.ApplyProposalAsync("wf-1", proposedJson, "no changes", "designer-1", CancellationToken.None);

        var merged = Assert.IsType<ProposalMergeOutcomeMerged>(result);
        Assert.StartsWith("rev-", merged.DraftRevisionAfterMerge, StringComparison.Ordinal);
        Assert.NotEqual("initial-import", merged.DraftRevisionAfterMerge);
        Assert.DoesNotMatch(@"^rev-\d+$", merged.DraftRevisionAfterMerge);
    }
}
