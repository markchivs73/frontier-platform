using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler;
using Frontier.Platform.Workflow.Compiler.Storage;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Unit tests for the S9.10 granular merge (<see cref="ProposalMergeService.ApplyApprovedChangesAsync"/>, doc 14 §4.1).</summary>
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
