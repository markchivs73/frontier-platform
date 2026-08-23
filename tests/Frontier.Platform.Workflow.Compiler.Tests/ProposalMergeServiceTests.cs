using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Storage;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

public sealed class ProposalMergeServiceTests
{
    private static readonly IReadOnlyList<string> EmptyNodes = Array.Empty<string>();

    private readonly InMemoryDefinitionStore _store;
    private readonly MockDefinitionCompiler _compiler;
    private readonly NodeDiffService _diffService;
    private readonly ProposalMergeService _service;

    public ProposalMergeServiceTests()
    {
        _store = new InMemoryDefinitionStore();
        _compiler = new MockDefinitionCompiler();
        _diffService = new NodeDiffService();
        _service = new ProposalMergeService(_store, _compiler, _diffService);
    }

    [Fact]
    public async Task ApplyProposalAsync_ValidProposal_NoConflicts_Succeeds()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedDefinition = draft.Definition;
        var proposedJson = JsonSerializer.Serialize(proposedDefinition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "No changes",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeMerged>(result);
        var merged = (ProposalMergeOutcomeMerged)result;
        Assert.NotEmpty(merged.DraftRevisionAfterMerge);
        Assert.NotNull(merged.UpdatedDraft);
        Assert.Equal("designer-1", merged.UpdatedDraft.LastEditedBy);
    }

    [Fact]
    public async Task ApplyProposalAsync_MalformedJson_ReturnsValidationBlocked()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        const string malformedJson = "{invalid json";

        var result = await _service.ApplyProposalAsync(
            workflowId,
            malformedJson,
            "Proposed changes",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(result);
        var blocked = (ProposalMergeOutcomeValidationBlocked)result;
        Assert.NotEmpty(blocked.BlockingFindings);
        Assert.Contains(blocked.BlockingFindings, f => f.RuleId == "proposal-parse-failed");
    }

    [Fact]
    public async Task ApplyProposalAsync_JsonDeserializesToNull_ReturnsValidationBlocked()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        const string nullJson = "null";

        var result = await _service.ApplyProposalAsync(
            workflowId,
            nullJson,
            "Proposed changes",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(result);
        var blocked = (ProposalMergeOutcomeValidationBlocked)result;
        Assert.Contains(blocked.BlockingFindings, f => f.RuleId == "proposal-null");
    }

    [Fact]
    public async Task ApplyProposalAsync_DraftNotFound_ReturnsValidationBlocked()
    {
        const string workflowId = "wf-nonexistent";
        var definition = CreateMinimalDefinition("wf-nonexistent");
        var proposedJson = JsonSerializer.Serialize(definition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "Proposed changes",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(result);
        var blocked = (ProposalMergeOutcomeValidationBlocked)result;
        Assert.Contains(blocked.BlockingFindings, f => f.RuleId == "draft-not-found");
    }

    [Fact]
    public async Task ApplyProposalAsync_ConflictingNodeModifications_ReturnsConflict()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedDefinition = new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Modified Workflow",
            Nodes = new[]
            {
                new AgentTaskNode
                {
                    NodeId = "gen-scope",
                    Role = "modified-role",
                    InstructionsRef = "scope-gen",
                    InputContractType = "BriefArtifact",
                    OutputContractType = "SummaryArtifact",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "engagement-1",
                        AgentRole = "modified-role",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            }.ToList(),
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };

        var proposedJson = JsonSerializer.Serialize(proposedDefinition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "Modified existing node",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeConflict>(result);
        var conflict = (ProposalMergeOutcomeConflict)result;
        Assert.NotEmpty(conflict.Conflicts);
        Assert.Contains(conflict.Conflicts, c => c.NodeId == "gen-scope");
    }

    // SerializeNode(node == null) — a real diff can never report a "conflict" nodeId that's absent
    // from both sides (conflicts come only from NodesModified, which by construction means the id
    // exists in both definitions), so a stubbed INodeDiffService is required to force the id-not-found
    // path and hit the null branch (S9.24 branch-coverage gap: line 285).
    [Fact]
    public async Task ApplyProposalAsync_ConflictNodeAbsentFromBothSides_SerializesAsNullLiteral()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedDefinition = draft.Definition;
        var proposedJson = JsonSerializer.Serialize(proposedDefinition, CanonicalProfile.Options);

        var fakeDiff = new Mock<INodeDiffService>();
        fakeDiff
            .Setup(d => d.Compute(It.IsAny<WorkflowDefinition>(), It.IsAny<WorkflowDefinition>()))
            .Returns(new WorkflowDefinitionDiff
            {
                NodesAdded = [],
                NodesRemoved = [],
                NodesModified = ["ghost-node"],
                EdgesAdded = [],
                EdgesRemoved = [],
                EdgesModified = [],
            });

        var service = new ProposalMergeService(_store, _compiler, fakeDiff.Object);

        var result = await service.ApplyProposalAsync(workflowId, proposedJson, "reasoning", "designer-1", CancellationToken.None);

        var conflict = Assert.IsType<ProposalMergeOutcomeConflict>(result);
        var ghost = Assert.Single(conflict.Conflicts, c => c.NodeId == "ghost-node");
        Assert.Equal("null", ghost.DesignerVersion);
        Assert.Equal("null", ghost.AgentProposedVersion);
    }

    // GenerateNewRevision's int.TryParse false branch — every other revision test uses a numeric
    // "rev-N" suffix; this uses a non-numeric suffix so parsing fails and falls back to the GUID
    // revision (S9.24 branch-coverage gap: line 303).
    [Fact]
    public async Task ApplyProposalAsync_NonNumericRevisionSuffix_FallsBackToGuidRevision()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId) with { DraftRevision = "rev-abc" };
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(workflowId, proposedJson, "Change", "designer-1", CancellationToken.None);

        var merged = Assert.IsType<ProposalMergeOutcomeMerged>(result);
        Assert.Matches("^rev-[0-9a-f]{32}$", merged.DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyProposalAsync_ValidationFailure_ReturnsValidationBlocked()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        _compiler.SetValidationFailure("test-rule", "Test validation failed");

        var proposedDefinition = draft.Definition;
        var proposedJson = JsonSerializer.Serialize(proposedDefinition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "Proposed changes",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeValidationBlocked>(result);
        var blocked = (ProposalMergeOutcomeValidationBlocked)result;
        Assert.NotEmpty(blocked.BlockingFindings);
        Assert.Contains(blocked.BlockingFindings, f => f.RuleId == "test-rule");
    }

    [Fact]
    public async Task ApplyProposalAsync_NullWorkflowId_Throws()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var json = JsonSerializer.Serialize(definition, CanonicalProfile.Options);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ApplyProposalAsync(null!, json, "reasoning", "designer-1", CancellationToken.None));

        Assert.Equal("workflowId", ex.ParamName);
    }

    [Fact]
    public async Task ApplyProposalAsync_EmptyWorkflowId_Throws()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var json = JsonSerializer.Serialize(definition, CanonicalProfile.Options);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ApplyProposalAsync("", json, "reasoning", "designer-1", CancellationToken.None));

        Assert.Contains("workflowId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyProposalAsync_NullProposedJson_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ApplyProposalAsync("wf-1", null!, "reasoning", "designer-1", CancellationToken.None));

        Assert.Equal("proposedDefinitionJson", ex.ParamName);
    }

    [Fact]
    public async Task ApplyProposalAsync_NullReasoning_Throws()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var json = JsonSerializer.Serialize(definition, CanonicalProfile.Options);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ApplyProposalAsync("wf-1", json, null!, "designer-1", CancellationToken.None));

        Assert.Equal("agentReasoning", ex.ParamName);
    }

    [Fact]
    public async Task ApplyProposalAsync_NullDesignerId_Throws()
    {
        var definition = CreateMinimalDefinition("wf-1");
        var json = JsonSerializer.Serialize(definition, CanonicalProfile.Options);

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ApplyProposalAsync("wf-1", json, "reasoning", null!, CancellationToken.None));

        Assert.Equal("designerId", ex.ParamName);
    }

    [Fact]
    public async Task ApplyProposalAsync_IncrementsRevisionNumber()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        draft = draft with { DraftRevision = "rev-5" };
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedJson = JsonSerializer.Serialize(draft.Definition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "Change",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeMerged>(result);
        var merged = (ProposalMergeOutcomeMerged)result;
        Assert.Equal("rev-6", merged.DraftRevisionAfterMerge);
    }

    [Fact]
    public async Task ApplyProposalAsync_AddsNewNodes_WithoutConflict_Succeeds()
    {
        const string workflowId = "wf-test";
        var draft = CreateMinimalDraft(workflowId);
        await _store.SaveDraftAsync(workflowId, draft, "no-etag", CancellationToken.None);

        var proposedDefinition = new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test Workflow",
            Nodes = new[]
            {
                draft.Definition.Nodes[0],
                new AgentTaskNode
                {
                    NodeId = "new-node",
                    Role = "new-role",
                    InstructionsRef = "new-instructions",
                    InputContractType = "NewInput",
                    OutputContractType = "NewOutput",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "engagement-1",
                        AgentRole = "new-role",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            }.ToList(),
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };

        var proposedJson = JsonSerializer.Serialize(proposedDefinition, CanonicalProfile.Options);

        var result = await _service.ApplyProposalAsync(
            workflowId,
            proposedJson,
            "Add new node",
            "designer-1",
            CancellationToken.None);

        Assert.IsType<ProposalMergeOutcomeMerged>(result);
        var merged = (ProposalMergeOutcomeMerged)result;
        Assert.Contains(merged.UpdatedDraft.Definition.Nodes, n => n.NodeId == "new-node");
    }

    private static DefinitionDraftDocument CreateMinimalDraft(string workflowId)
    {
        var definition = CreateMinimalDefinition(workflowId);

        return new DefinitionDraftDocument
        {
            Id = $"{workflowId}:draft",
            WorkflowId = workflowId,
            State = "draft",
            BaseVersion = 1,
            Definition = definition,
            DraftRevision = "rev-1",
            LastEditedBy = "user-1",
            LastEditedUtc = DateTime.UtcNow
        };
    }

    private static WorkflowDefinition CreateMinimalDefinition(string workflowId)
    {
        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            DefinitionVersion = 1,
            EngagementType = "advisory-sow",
            Name = "Test Workflow",
            Nodes = new[]
            {
                new AgentTaskNode
                {
                    NodeId = "gen-scope",
                    Role = "gen-scope",
                    InstructionsRef = "scope-gen",
                    InputContractType = "BriefArtifact",
                    OutputContractType = "SummaryArtifact",
                    ContextRequest = new ContextRequest
                    {
                        EngagementId = "engagement-1",
                        AgentRole = "gen-scope",
                        BaselineComponents = EmptyNodes,
                        DynamicFields = EmptyNodes
                    }
                }
            }.ToList(),
            Edges = new List<WorkflowEdge>(),
            DefinitionHash = "test-hash",
            Mode = ExecutionMode.OneShot
        };
    }

    private sealed class MockDefinitionCompiler : IDefinitionCompiler
    {
        private ValidationSeverity _failureSeverity = ValidationSeverity.Info;
        private string _failureRuleId = "";
        private string _failureMessage = "";
        private bool _shouldFail;

        public void SetValidationFailure(string ruleId, string message)
        {
            _shouldFail = true;
            _failureRuleId = ruleId;
            _failureMessage = message;
            _failureSeverity = ValidationSeverity.Error;
        }

        public Task<ValidationReport> ValidateAsync(
            WorkflowDefinition draft,
            string draftRevision,
            CancellationToken ct)
        {
            return Task.FromResult(new ValidationReport(
                WorkflowId: draft.WorkflowId,
                DraftRevision: draftRevision,
                ValidatedAtUtc: DateTime.UtcNow,
                Outcome: _shouldFail ? ValidationOutcome.Fail : ValidationOutcome.Pass,
                Findings: _shouldFail ? new[] {
                    new ValidationFinding(
                        RuleId: _failureRuleId,
                        Severity: _failureSeverity,
                        Message: _failureMessage)
                }.ToList().AsReadOnly() : Array.Empty<ValidationFinding>().AsReadOnly(),
                ResourceVersions: new Dictionary<string, string>().AsReadOnly()));
        }

        public IReadOnlyList<ValidationFinding> ValidateStructural(WorkflowDefinition draft)
        {
            if (_shouldFail)
            {
                return new[] {
                    new ValidationFinding(
                        RuleId: _failureRuleId,
                        Severity: _failureSeverity,
                        Message: _failureMessage)
                }.ToList().AsReadOnly();
            }

            return Array.Empty<ValidationFinding>().AsReadOnly();
        }

        public string ComputeDefinitionHash(WorkflowDefinition definition)
        {
            return "sha256:test";
        }
    }
}
