namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// How <see cref="CosmosDefinitionStore.ListWorkflowsAsync"/> folds one workflow's draft and version rows into its
/// catalogue entry, kept apart from the coverage-excluded adapter so the unit gate measures it (as
/// <see cref="DefinitionWriteRules"/> is). The entry's name, type and mode all come from one row, the
/// <em>authority</em>: the current published version, else the highest-numbered row. Before this the name was the
/// first non-blank name among the rows in whatever order Cosmos returned them (consumer S13.271). The draft's own
/// name and type ride alongside, so a caller can see a rename or retype that lives only on the draft.
/// </summary>
internal static class WorkflowCatalogueRules
{
    private const string Draft = "draft";
    private const string Published = "published";

    /// <summary>The catalogue entry for <paramref name="workflowId"/> from all of its draft and version rows.</summary>
    internal static WorkflowCatalogueSummary Summarise(string workflowId, IReadOnlyCollection<CatalogueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var byVersion = rows.OrderByDescending(r => r.DefinitionVersion ?? 0).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var published = byVersion.FirstOrDefault(r => r.State == Published);
        var authority = published ?? byVersion[0];
        var draft = rows.FirstOrDefault(r => r.State == Draft);
        return new WorkflowCatalogueSummary(
            workflowId, NameOf(workflowId, authority, byVersion), authority.DefinitionEngagementType,
            StatusOf(byVersion, published), published?.ApprovedUtc, published?.ApprovedBy, authority.DefinitionMode)
        {
            DraftName = draft?.DefinitionName,
            DraftEngagementType = draft?.DefinitionEngagementType,
        };
    }

    /// <summary>The authority's name; else the first non-blank name from the highest version down; else the id.</summary>
    internal static string NameOf(string workflowId, CatalogueRow authority, IEnumerable<CatalogueRow> byVersion)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!string.IsNullOrWhiteSpace(authority.DefinitionName)) return authority.DefinitionName;
        return byVersion.Select(r => r.DefinitionName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? workflowId;
    }

    /// <summary><c>published</c> with a current version; <c>retired</c> with versions but none current; else <c>draft</c>.</summary>
    internal static string StatusOf(IEnumerable<CatalogueRow> rows, CatalogueRow? published)
    {
        if (published is not null) return Published;
        return rows.Any(r => r.State != Draft) ? "retired" : Draft;
    }
}

/// <summary>One draft or version row of the catalogue's cross-partition query.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(Justification = "Storage POCO record with compiler-generated equality")]
internal sealed record CatalogueRow
{
    [System.Text.Json.Serialization.JsonPropertyName("workflowId")]
    public string WorkflowId { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string State { get; init; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("definitionVersion")]
    public int? DefinitionVersion { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("approvedUtc")]
    public DateTime? ApprovedUtc { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("approvedBy")]
    public string? ApprovedBy { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("definitionName")]
    public string? DefinitionName { get; init; }
    [System.Text.Json.Serialization.JsonPropertyName("definitionEngagementType")]
    public string? DefinitionEngagementType { get; init; }
    /// <summary>The definition's <c>mode</c> as its canonical wire string (<c>one_shot</c>, <c>dispatcher</c>).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("definitionMode")]
    public string? DefinitionMode { get; init; }
}
