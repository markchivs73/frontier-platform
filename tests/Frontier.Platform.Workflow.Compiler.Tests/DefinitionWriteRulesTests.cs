using Frontier.Platform.Workflow.Compiler.Storage;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// <see cref="DefinitionWriteRules"/> — the draft-save and publish lost-update decisions of
/// <see cref="CosmosDefinitionStore"/> (ADR-CD2, GOV-03), measured by the unit gate since S13.193.
/// </summary>
public sealed class DefinitionWriteRulesTests
{
    private const string WorkflowId = "wf-rules";

    [Fact]
    public void IsStaleSave_NoDraftYet_IsNotStale() =>
        Assert.False(DefinitionWriteRules.IsStaleSave(null, "rev-1"));

    [Fact]
    public void IsStaleSave_DraftAtTheEditedRevision_IsNotStale() =>
        Assert.False(DefinitionWriteRules.IsStaleSave(Draft("rev-1"), "rev-1"));

    [Theory]
    [InlineData("rev-2")]
    [InlineData("REV-1")]
    public void IsStaleSave_DraftMovedOn_IsStale(string expected) =>
        Assert.True(DefinitionWriteRules.IsStaleSave(Draft("rev-1"), expected));

    [Fact]
    public void Conflict_NamesTheWinnersRevisionAndDocument()
    {
        var winner = Draft("rev-9");

        var conflict = DefinitionWriteRules.Conflict(winner);

        Assert.Equal(("rev-9", "unknown"), (conflict.CurrentRevision, conflict.CurrentETag));
        Assert.Same(winner, conflict.CurrentDocument);
    }

    [Fact]
    public void ResolvePublishConflict_SameHash_ReusesTheStoredVersion()
    {
        var stored = Version("sha256:abc");

        Assert.Same(stored, DefinitionWriteRules.ResolvePublishConflict(stored, Version("sha256:abc")));
    }

    [Fact]
    public void ResolvePublishConflict_DifferentHash_RefusesThePublish()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DefinitionWriteRules.ResolvePublishConflict(Version("sha256:abc"), Version("sha256:def")));

        Assert.Contains("Version 2 already exists", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePublishConflict_ConflictButNothingReadable_RefusesThePublish() =>
        Assert.Throws<InvalidOperationException>(() => DefinitionWriteRules.ResolvePublishConflict(null, Version("sha256:abc")));

    [Fact]
    public void ResolvePublishConflict_NullPublishing_Throws() =>
        Assert.Throws<ArgumentNullException>(() => DefinitionWriteRules.ResolvePublishConflict(null, null!));

    private static DefinitionDraftDocument Draft(string revision) =>
        ChatDesignerServiceTests.CreateMinimalDraft(WorkflowId) with { DraftRevision = revision };

    private static DefinitionVersionDocument Version(string hash) => new()
    {
        Id = $"{WorkflowId}:v2",
        WorkflowId = WorkflowId,
        State = "published",
        DefinitionVersion = 2,
        DefinitionHash = hash,
        Definition = ChatDesignerServiceTests.CreateMinimalDraft(WorkflowId).Definition,
        ProposedBy = "user:mark",
        ApprovedBy = "user:sarah",
        ProposedUtc = DateTime.UnixEpoch,
        ApprovedUtc = DateTime.UnixEpoch,
        ValidationReportRef = "ref",
    };
}
