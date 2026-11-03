using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler;
using Frontier.Platform.Workflow.Compiler.Storage;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// Unit tests for the S9.10 granular merge (doc 14 §4.1). The tests of the newest-turn overload stay
/// until it is removed, so they call the obsolete member on purpose.
/// </summary>
#pragma warning disable CS0618 // the newest-turn overload is obsolete (UI-05) but still shipped
public sealed class ProposalMergeServiceGranularTests
{
    [Fact]
    public void ApplyApproved_AppliesOnlyApprovedNodeAdd_LeavesUnapprovedEdgeOut()
    {
        var current = Def([Node("scope")], []);
        var proposed = Def([Node("scope"), Node("gate-1")], [Edge("scope", "gate-1")]);

        var merged = ProposalMergeService.ApplyApproved(current, proposed, Ids("node:added:gate-1"));

        Assert.Contains(merged.Nodes, n => n.NodeId == "gate-1");
        Assert.Empty(merged.Edges); // edge add was not approved
    }

    [Fact]
    public void ApplyApproved_RemovesApprovedNode_KeepsOthers()
    {
        var current = Def([Node("scope"), Node("old")], []);
        var proposed = Def([Node("scope")], []);

        var merged = ProposalMergeService.ApplyApproved(current, proposed, Ids("node:removed:old"));

        Assert.DoesNotContain(merged.Nodes, n => n.NodeId == "old");
        Assert.Contains(merged.Nodes, n => n.NodeId == "scope");
    }

    [Fact]
    public void ApplyApproved_FieldOnlyEditOfferedByTheChangeSet_IsApplied()
    {
        // GOV-02: an edit outside the old key fields (here instructions_ref and output contract)
        // must surface as node:modified, and approving it must land the agent's node.
        var current = Def([Node("scope")], []);
        var proposed = Def([Node("scope") with { InstructionsRef = "i-v2", OutputContractType = "Out2" }], []);
        var changes = ProposalChangeSetBuilder.Build(new NodeDiffService().Compute(current, proposed), current, proposed);

        var merged = ProposalMergeService.ApplyApproved(current, proposed, Ids([.. changes.Select(c => c.ChangeId)]));

        Assert.Equal(["node:modified:scope"], changes.Select(c => c.ChangeId));
        var node = Assert.IsType<AgentTaskNode>(Assert.Single(merged.Nodes));
        Assert.Equal(("i-v2", "Out2"), (node.InstructionsRef, node.OutputContractType));
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_HappyPath_AppliesSubsetAndBumpsRevision()
    {
        var (store, service) = await SetupAsync(passValidation: true);

        var outcome = await service.ApplyApprovedChangesAsync("wf", ["node:added:gate-1"], "rev-1", "d", CancellationToken.None);

        var merged = Assert.IsType<ProposalMergeOutcomeMerged>(outcome);
        Assert.Contains(merged.UpdatedDraft.Definition.Nodes, n => n.NodeId == "gate-1");
        Assert.Empty(merged.UpdatedDraft.Definition.Edges);
        Assert.NotEqual("rev-1", merged.DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_StaleBase_ReturnsConflict()
    {
        var (_, service) = await SetupAsync(passValidation: true);

        var outcome = await service.ApplyApprovedChangesAsync("wf", ["node:added:gate-1"], "stale-rev", "d", CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeConflict>(outcome);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_MergedResultFailsValidation_ReturnsBlocked()
    {
        var (_, service) = await SetupAsync(passValidation: false);

        var outcome = await service.ApplyApprovedChangesAsync("wf", ["node:added:gate-1"], "rev-1", "d", CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_NoProposal_ReturnsBlocked()
    {
        var store = new InMemoryDefinitionStore();
        await store.SaveDraftAsync("wf", Draft(Def([Node("scope")], [])), "no-etag", CancellationToken.None);
        var service = new ProposalMergeService(store, Validator(passValidation: true), new NodeDiffService());

        var outcome = await service.ApplyApprovedChangesAsync("wf", [], "rev-1", "d", CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_NoDraft_ReturnsBlocked()
    {
        var service = new ProposalMergeService(new InMemoryDefinitionStore(), Validator(passValidation: true), new NodeDiffService());

        var outcome = await service.ApplyApprovedChangesAsync("missing", [], "rev-1", "d", CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_SaveLosesRevisionGuard_ReturnsConflictNotMerged()
    {
        // GOV-09: a save that loses the revision guard leaves the draft unchanged, so it is a conflict.
        var (store, service) = await SetupAsync(passValidation: true);
        store.NextSaveConflict = SaveConflict("rev-9");

        var outcome = await service.ApplyApprovedChangesAsync("wf", ["node:added:gate-1"], "rev-1", "d", CancellationToken.None);

        Assert.Equal("rev-9", Assert.IsType<ProposalMergeOutcomeConflict>(outcome).DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurn_MergesTheClickedTurnNotTheNewest()
    {
        // UI-05 / GOV-10: an older card's Apply must merge that card's proposal.
        var store = await StoreWithDraftAsync();
        await store.PersistDesignTurnAsync(TurnWithProposal("wf", 1, Def([Node("scope") with { InstructionsRef = "i-turn-1" }], [])), CancellationToken.None);
        await store.PersistDesignTurnAsync(TurnWithProposal("wf", 2, Def([Node("scope") with { InstructionsRef = "i-turn-2" }], [])), CancellationToken.None);

        var outcome = await Service(store).ApplyApprovedChangesAsync("wf", "wf:turn:1", "rev-1", ["node:modified:scope"], "d", CancellationToken.None);

        var merged = Assert.IsType<ProposalMergeOutcomeMerged>(outcome);
        Assert.Equal("i-turn-1", Assert.IsType<AgentTaskNode>(Assert.Single(merged.UpdatedDraft.Definition.Nodes)).InstructionsRef);
        Assert.Equal("rev-2", merged.DraftRevisionAfterMerge);
    }

    [Theory]
    [InlineData("wf:turn:404")] // no such turn
    [InlineData("other:turn:1")] // a turn of another workflow
    [InlineData("wf:turn:2")] // a turn without a proposal
    [InlineData("wf:turn:3")] // a turn whose proposal is unreadable
    public async Task ApplyApprovedChangesAsync_ForTurnWithoutUsableProposal_ReturnsBlocked(string turnId)
    {
        var store = await StoreWithDraftAsync();
        await store.PersistDesignTurnAsync(TurnWithProposal("other", 1, Def([Node("scope"), Node("gate-1")], [])), CancellationToken.None);
        await store.PersistDesignTurnAsync(TurnWithProposal("wf", 2, Def([], [])) with { AgentProposalJson = null }, CancellationToken.None);
        await store.PersistDesignTurnAsync(TurnWithProposal("wf", 3, Def([], [])) with { AgentProposalJson = "{not json" }, CancellationToken.None);

        var outcome = await Service(store).ApplyApprovedChangesAsync("wf", turnId, "rev-1", ["node:added:gate-1"], "d", CancellationToken.None);

        var blocked = Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
        Assert.Equal("no-proposal", Assert.Single(blocked.BlockingFindings).RuleId);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurnWithStaleBase_ReturnsConflict()
    {
        var (_, service) = await SetupAsync(passValidation: true);

        var outcome = await service.ApplyApprovedChangesAsync("wf", "wf:turn:1", "stale-rev", ["node:added:gate-1"], "d", CancellationToken.None);

        Assert.Equal("rev-1", Assert.IsType<ProposalMergeOutcomeConflict>(outcome).DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurnWhenSaveLosesRevisionGuard_ReturnsConflict()
    {
        var (store, service) = await SetupAsync(passValidation: true);
        store.NextSaveConflict = SaveConflict("rev-9");

        var outcome = await service.ApplyApprovedChangesAsync("wf", "wf:turn:1", "rev-1", ["node:added:gate-1"], "d", CancellationToken.None);

        Assert.Equal("rev-9", Assert.IsType<ProposalMergeOutcomeConflict>(outcome).DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurnWhoseMergeFailsValidation_ReturnsBlocked()
    {
        var (_, service) = await SetupAsync(passValidation: false);

        var outcome = await service.ApplyApprovedChangesAsync("wf", "wf:turn:1", "rev-1", ["node:added:gate-1"], "d", CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurnWithNoDraft_ReturnsBlocked()
    {
        var outcome = await Service(new InMemoryDefinitionStore()).ApplyApprovedChangesAsync(
            "missing", "missing:turn:1", "rev-1", [], "d", CancellationToken.None);

        Assert.Equal("draft-not-found", Assert.Single(Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome).BlockingFindings).RuleId);
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurnWithEmptyTurnId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(new InMemoryDefinitionStore()).ApplyApprovedChangesAsync("wf", "", "rev-1", [], "d", CancellationToken.None));
    }

    [Fact]
    public async Task ApplyApprovedChangesAsync_ForTurn_DefaultImplementationThrowsNotSupported()
    {
        IProposalMergeService legacy = new LegacyMergeService();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => legacy.ApplyApprovedChangesAsync("wf", "wf:turn:1", "rev-1", [], "d", CancellationToken.None));

        Assert.Contains(nameof(LegacyMergeService), ex.Message, StringComparison.Ordinal);
    }

    private static async Task<InMemoryDefinitionStore> StoreWithDraftAsync()
    {
        var store = new InMemoryDefinitionStore();
        await store.SaveDraftAsync("wf", Draft(Def([Node("scope")], [])), "no-etag", CancellationToken.None);
        return store;
    }

    private static ProposalMergeService Service(InMemoryDefinitionStore store) =>
        new(store, Validator(passValidation: true), new NodeDiffService());

    private static SaveDraftResultConflict SaveConflict(string winningRevision) =>
        new("etag", winningRevision, Draft(Def([Node("scope")], [])) with { DraftRevision = winningRevision });

    private static async Task<(InMemoryDefinitionStore Store, ProposalMergeService Service)> SetupAsync(bool passValidation)
    {
        var store = new InMemoryDefinitionStore();
        await store.SaveDraftAsync("wf", Draft(Def([Node("scope")], [])), "no-etag", CancellationToken.None);

        var proposed = Def([Node("scope"), Node("gate-1")], [Edge("scope", "gate-1")]);
        await store.PersistDesignTurnAsync(TurnWithProposal("wf", 1, proposed), CancellationToken.None);

        return (store, new ProposalMergeService(store, Validator(passValidation), new NodeDiffService()));
    }

    private static DefinitionValidator Validator(bool passValidation) =>
        passValidation
            ? new DefinitionValidator(Array.Empty<IDefinitionValidationRule>())
            : new DefinitionValidator([new AlwaysFailsRule()]);

    private static HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.Ordinal);

    private static AgentTaskNode Node(string id) => new()
    {
        NodeId = id,
        Role = "r",
        InstructionsRef = "i",
        InputContractType = "In",
        OutputContractType = "Out",
        ContextRequest = new ContextRequest
        {
            EngagementId = "e",
            AgentRole = "r",
            BaselineComponents = [],
            DynamicFields = [],
        },
    };

    private static WorkflowEdge Edge(string from, string to) =>
        new() { FromNodeId = from, ToNodeId = to, Kind = EdgeKind.Control };

    private static WorkflowDefinition Def(IEnumerable<WorkflowNode> nodes, IEnumerable<WorkflowEdge> edges) => new()
    {
        WorkflowId = "wf",
        DefinitionVersion = 1,
        EngagementType = "advisory-sow",
        Name = "n",
        Nodes = nodes.ToList(),
        Edges = edges.ToList(),
        DefinitionHash = "h",
        Mode = ExecutionMode.OneShot,
    };

    private static DefinitionDraftDocument Draft(WorkflowDefinition def) => new()
    {
        Id = "wf:draft",
        WorkflowId = "wf",
        State = "draft",
        BaseVersion = 1,
        Definition = def,
        DraftRevision = "rev-1",
        LastEditedBy = "u",
        LastEditedUtc = DateTime.UtcNow,
    };

    private static DesignTurnDocument TurnWithProposal(string workflowId, int turnNumber, WorkflowDefinition proposed) => new()
    {
        Id = $"{workflowId}:turn:{turnNumber}",
        WorkflowId = workflowId,
        DraftId = $"{workflowId}:draft",
        TurnNumber = turnNumber,
        DesignerId = "d",
        CreatedAtUtc = DateTime.UtcNow,
        DesignerMessage = "m",
        DraftRevisionAtTurn = "rev-1",
        AgentProposalJson = JsonSerializer.Serialize(proposed, CanonicalProfile.Options),
        ProposalReasoningJson = "r",
    };

    /// <summary>An implementer that predates the turn-scoped overload (only the original members).</summary>
    private sealed class LegacyMergeService : IProposalMergeService
    {
        public Task<ProposalMergeOutcome> ApplyProposalAsync(string workflowId, string proposedDefinitionJson, string agentReasoning, string designerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ProposalMergeOutcome> ApplyApprovedChangesAsync(string workflowId, IReadOnlyList<string> approvedChangeIds, string expectedRevision, string designerId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class AlwaysFailsRule : IDefinitionValidationRule
    {
        public string RuleId => "test.always-fails";
        public RuleTier Tier => RuleTier.Pure;
        public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

        public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ValidationFinding>>(
                [new ValidationFinding("test.always-fails", ValidationSeverity.Error, "boom")]);
    }
}
#pragma warning restore CS0618
