using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Storage;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.199: structure.unique-artifact-keys refuses two nodes that produce the same section, so
/// neither publish nor the designer merge accepts a definition whose steps overwrite each other.
/// </summary>
public sealed class StructureUniqueArtifactKeysRuleTests
{
    [Fact]
    public async Task DistinctKeys_ReturnsEmpty()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("gen-scope", sectionKey: "scope"), S930Fixtures.Agent("gen-risks", sectionKey: "risks")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task KeylessNodes_AreNotCompared()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", sectionKey: null), S930Fixtures.Agent("b", sectionKey: null), S930Fixtures.Agent("c", sectionKey: " "), S930Fixtures.Gate("gate-1")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task TwoAgentsSharingAKey_ReturnsErrorOnTheSecond()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("gen-scope", sectionKey: "scope"), S930Fixtures.Agent("gen-scope-2", sectionKey: "scope")]);

        var finding = Assert.Single(await EvaluateAsync(definition));
        Assert.Equal("structure.unique-artifact-keys", finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("gen-scope-2", finding.NodeId);
        Assert.Equal("artifact_key", finding.FieldPath);
        Assert.Contains("'scope'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'gen-scope'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentAndMcpToolSharingAKey_ReturnsFinding()
    {
        // The orchestrator writes a section for an mcp_tool step too, so the clash is workflow-wide.
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("gen-scope", sectionKey: "scope"), S930Fixtures.Mcp("push") with { ArtifactKey = "scope" }]);

        var finding = Assert.Single(await EvaluateAsync(definition));
        Assert.Equal("push", finding.NodeId);
    }

    [Fact]
    public async Task ThreeNodesSharingAKey_EachLaterOneNamesTheFirst()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", sectionKey: "scope"), S930Fixtures.Agent("b", sectionKey: "scope"), S930Fixtures.Agent("c", sectionKey: "scope")]);

        var findings = await EvaluateAsync(definition);

        Assert.Equal(["b", "c"], findings.Select(f => f.NodeId));
        Assert.All(findings, f => Assert.Contains("'a'", f.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeysDifferingOnlyInCase_AreDistinct()
    {
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", sectionKey: "scope"), S930Fixtures.Agent("b", sectionKey: "Scope")]);

        Assert.Empty(await EvaluateAsync(definition));
    }

    [Fact]
    public async Task CompilerStructuralValidation_RefusesTheDuplicate()
    {
        // Publish and the designer merge both gate on the compiler's report; the rule must be in it.
        var compiler = new DefinitionValidator([new StructureUniqueArtifactKeysRule()]);
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", sectionKey: "scope"), S930Fixtures.Agent("b", sectionKey: "scope")]);

        var findings = compiler.ValidateStructural(definition);

        Assert.Contains(findings, f => f.RuleId == "structure.unique-artifact-keys" && f.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task CompilerValidateAsync_DuplicateKey_Fails()
    {
        var compiler = new DefinitionValidator([new StructureUniqueArtifactKeysRule()]);
        var definition = S930Fixtures.Build(
            [S930Fixtures.Agent("a", sectionKey: "scope"), S930Fixtures.Agent("b", sectionKey: "scope")]);

        var report = await compiler.ValidateAsync(definition, "rev-1", storedSchemaVersion: null, CancellationToken.None);

        Assert.Equal(ValidationOutcome.Fail, report.Outcome);
        Assert.Contains(report.Findings, f => f.RuleId == "structure.unique-artifact-keys");
    }

    [Fact]
    public async Task ProposalMerge_AddingANodeOnATakenKey_IsBlocked()
    {
        var store = new InMemoryDefinitionStore();
        var draft = S930Fixtures.Build([S930Fixtures.Agent("a", sectionKey: "scope")]);
        await store.SaveDraftAsync(draft.WorkflowId, Draft(draft), "no-etag", CancellationToken.None);
        var service = new ProposalMergeService(store, new DefinitionValidator([new StructureUniqueArtifactKeysRule()]), new NodeDiffService());
        var proposal = draft with { Nodes = [.. draft.Nodes, S930Fixtures.Agent("b", sectionKey: "scope")] };

        var outcome = await service.ApplyProposalAsync(draft.WorkflowId, JsonSerializer.Serialize(proposal, CanonicalProfile.Options), "add b", "designer-1", CancellationToken.None);

        var blocked = Assert.IsType<ProposalMergeOutcomeValidationBlocked>(outcome);
        var finding = Assert.Single(blocked.BlockingFindings);
        Assert.Equal(("structure.unique-artifact-keys", "b"), (finding.RuleId, finding.NodeId));
    }

    private static DefinitionDraftDocument Draft(WorkflowDefinition definition) => new()
    {
        Id = $"{definition.WorkflowId}:draft",
        WorkflowId = definition.WorkflowId,
        State = "draft",
        BaseVersion = 1,
        Definition = definition,
        DraftRevision = "rev-1",
        LastEditedBy = "user-1",
        LastEditedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(WorkflowDefinition definition) =>
        new StructureUniqueArtifactKeysRule().EvaluateAsync(new DefinitionValidationContext(definition), CancellationToken.None);
}
