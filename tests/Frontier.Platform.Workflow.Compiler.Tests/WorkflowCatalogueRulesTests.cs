using Frontier.Platform.Workflow.Compiler.Storage;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// <see cref="WorkflowCatalogueRules"/> — how a workflow's draft and version rows fold into its A1 catalogue entry
/// (consumer S13.271): one authority row for name, type and mode, and the draft's own name and type alongside.
/// </summary>
public sealed class WorkflowCatalogueRulesTests
{
    private const string WorkflowId = "wf-cat";
    private static readonly DateTime Approved = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private static CatalogueRow Draft(string? name, string? type) => new()
    {
        WorkflowId = WorkflowId, Id = $"{WorkflowId}:draft", State = "draft", DefinitionName = name, DefinitionEngagementType = type, DefinitionMode = "one_shot",
    };

    private static CatalogueRow Version(int version, string state, string? name, string? type, string mode = "one_shot") => new()
    {
        WorkflowId = WorkflowId, Id = $"{WorkflowId}:v{version}", State = state, DefinitionVersion = version,
        ApprovedUtc = Approved.AddDays(version), ApprovedBy = $"user:approver-{version}",
        DefinitionName = name, DefinitionEngagementType = type, DefinitionMode = mode,
    };

    [Fact]
    public void Summarise_DraftRenamedAndRetypedAfterPublish_KeepsThePublishedValuesAndCarriesTheDrafts()
    {
        // The draft row listed first, as Cosmos may return it: the name used to be whichever row came first.
        var rows = new[] { Draft("Intake v2", "advisory-sow"), Version(1, "published", "Intake", "helpdesk", "dispatcher") };

        var s = WorkflowCatalogueRules.Summarise(WorkflowId, rows);

        Assert.Equal(("Intake", "helpdesk", "dispatcher"), (s.Name, s.EngagementType, s.ExecutionMode));
        Assert.Equal(("Intake v2", "advisory-sow"), (s.DraftName, s.DraftEngagementType));
        Assert.Equal(("published", Approved.AddDays(1), "user:approver-1"), (s.Status, s.LastPublishedAt, s.LastPublishedBy));
    }

    [Fact]
    public void Summarise_RowOrder_DoesNotChangeTheEntry()
    {
        var rows = new[] { Version(1, "superseded", "Old", "helpdesk"), Draft("Next", "helpdesk"), Version(2, "published", "Current", "helpdesk") };

        var forward = WorkflowCatalogueRules.Summarise(WorkflowId, rows);
        var backward = WorkflowCatalogueRules.Summarise(WorkflowId, rows.Reverse().ToArray());

        Assert.Equal(forward, backward);
        Assert.Equal("Current", forward.Name);
    }

    [Fact]
    public void Summarise_DraftOnly_IsADraftNamedByIt()
    {
        var s = WorkflowCatalogueRules.Summarise(WorkflowId, [Draft("Intake", "helpdesk")]);

        Assert.Equal(("Intake", "helpdesk", "draft"), (s.Name, s.EngagementType, s.Status));
        Assert.Equal(("Intake", "helpdesk"), (s.DraftName, s.DraftEngagementType));
        Assert.Null(s.LastPublishedAt);
    }

    [Fact]
    public void Summarise_AllVersionsRetiredAndNoDraft_IsRetiredFromTheHighestVersion()
    {
        var s = WorkflowCatalogueRules.Summarise(WorkflowId, [Version(1, "retired", "First", "helpdesk"), Version(2, "retired", "Second", "advisory-sow")]);

        Assert.Equal(("Second", "advisory-sow", "retired"), (s.Name, s.EngagementType, s.Status));
        Assert.Null(s.DraftName);
        Assert.Null(s.DraftEngagementType);
        Assert.Equal<(DateTime?, string?)>((null, null), (s.LastPublishedAt, s.LastPublishedBy));
    }

    [Fact]
    public void Summarise_NullRows_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WorkflowCatalogueRules.Summarise(WorkflowId, null!));

    [Fact]
    public void NameOf_AuthorityUnnamed_FallsBackToTheHighestNamedRow()
    {
        var authority = Version(3, "published", " ", "helpdesk");
        var byVersion = new[] { authority, Version(2, "superseded", "Second", "helpdesk"), Version(1, "superseded", "First", "helpdesk") };

        Assert.Equal("Second", WorkflowCatalogueRules.NameOf(WorkflowId, authority, byVersion));
    }

    [Fact]
    public void NameOf_NoRowNamed_IsTheWorkflowId()
    {
        var authority = Draft(null, "helpdesk");

        Assert.Equal(WorkflowId, WorkflowCatalogueRules.NameOf(WorkflowId, authority, [authority]));
    }

    [Fact]
    public void NameOf_NullAuthority_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WorkflowCatalogueRules.NameOf(WorkflowId, null!, []));

    [Fact]
    public void StatusOf_DraftOnly_IsDraft() =>
        Assert.Equal("draft", WorkflowCatalogueRules.StatusOf([Draft("Intake", "helpdesk")], null));

    [Fact]
    public void StatusOf_SupersededButNoneCurrent_IsRetired() =>
        Assert.Equal("retired", WorkflowCatalogueRules.StatusOf([Draft("Intake", "helpdesk"), Version(1, "superseded", "Intake", "helpdesk")], null));

    [Fact]
    public void StatusOf_WithACurrentVersion_IsPublished()
    {
        var published = Version(1, "published", "Intake", "helpdesk");

        Assert.Equal("published", WorkflowCatalogueRules.StatusOf([published], published));
    }
}
