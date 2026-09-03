using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.58 D2: the consumer asks what a definition's entry node needs *before* scheduling, so a start
/// that cannot satisfy it is refused rather than failing at node 1. Entry detection is control-graph
/// knowledge that lives here; the alternative was the consumer re-deriving the walk, which is the
/// six-copies defect ADR-PA11 was written about.
/// </summary>
public sealed class WorkflowEntryInspectorTests
{
    [Fact]
    public void GetEntry_SingleAgentTaskEntry_ReturnsItsRequiredDynamicFields()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("entry", dynamicFields: ["engagement_brief"])]);

        var entry = new WorkflowEntryInspector().GetEntry(definition);

        Assert.NotNull(entry);
        Assert.Equal("entry", entry.NodeId);
        Assert.Equal("BriefArtifact", entry.InputContractType);
        Assert.Equal(["engagement_brief"], entry.RequiredDynamicFields);
    }

    /// <summary>
    /// A workflow needing no dynamic context is startable with no input at all — the distinction the
    /// caller's refusal rule turns on, and the one the first design got wrong by keying on the entry
    /// *contract* instead.
    /// </summary>
    [Fact]
    public void GetEntry_EntryRequestingNoDynamicFields_ReturnsAnEmptyRequirement()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("entry", dynamicFields: [])]);

        var entry = new WorkflowEntryInspector().GetEntry(definition);

        Assert.NotNull(entry);
        Assert.Empty(entry.RequiredDynamicFields);
    }

    /// <summary>
    /// Declines on the same conditions as <see cref="TestRunInputSchemaProvider"/>: a definition with
    /// no single resolvable agent entry has no one node whose needs can be stated, and guessing would
    /// be worse than saying so.
    /// </summary>
    [Fact]
    public void GetEntry_NoNodes_ReturnsNull()
    {
        Assert.Null(new WorkflowEntryInspector().GetEntry(S930Fixtures.Build([])));
    }

    [Fact]
    public void GetEntry_TwoEntryCandidates_ReturnsNull()
    {
        var definition = S930Fixtures.Build([S930Fixtures.Agent("a"), S930Fixtures.Agent("b")]);

        Assert.Null(new WorkflowEntryInspector().GetEntry(definition));
    }

    [Fact]
    public void GetEntry_NullDefinition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkflowEntryInspector().GetEntry(null!));
    }
}
