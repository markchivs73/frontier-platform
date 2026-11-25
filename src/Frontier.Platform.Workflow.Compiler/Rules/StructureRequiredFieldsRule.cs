namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.required-fields: the definition's name and engagement type are present and not blank,
/// each reported as an Error (S13.206, option C). Doc 13 §4.2, Phase 1 rule catalogue. A brand-new draft
/// starts with both blank, so its validation report fails and <c>ProposePublishAsync</c> refuses that report
/// until both are set. S13.238 (triage 2026-11-24): a missing or blank description is a Warning, which does not
/// block a proposal; each malformed tag (<see cref="DefinitionTagFormat"/>) and more than ten tags are Errors.
/// </summary>
public sealed class StructureRequiredFieldsRule : PureTierRule
{
    /// <inheritdoc />
    public override string RuleId => "structure.required-fields";

    /// <inheritdoc />
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var findings = new List<ValidationFinding>();
        AddIfBlank(findings, ctx.Definition.Name, "name", "a name");
        AddIfBlank(findings, ctx.Definition.EngagementType, "engagement_type", "an engagement type");
        AddIfNoDescription(findings, ctx.Definition.Description);
        findings.AddRange(TagFindings(ctx.Definition.Tags ?? []));
        return findings;
    }

    /// <summary>A Warning when the description is missing or blank: worth having, never a reason to refuse a publish.</summary>
    private void AddIfNoDescription(List<ValidationFinding> findings, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            findings.Add(new ValidationFinding(RuleId, ValidationSeverity.Warning,
                "The definition has no description; 'description' says what the workflow is for.", FieldPath: "description"));
        }
    }

    /// <summary>
    /// Whether <paramref name="finding"/> is one of this rule's tag findings. The chat designer's merge applies only
    /// nodes and edges, so a proposal's tags never reach the draft: these findings must not drive the repair pass
    /// or block Apply (S13.238). They still block a publish.
    /// </summary>
    internal static bool IsTagFinding(ValidationFinding finding) =>
        finding.RuleId == "structure.required-fields"
        && finding.FieldPath is { } path
        && (path == "tags" || path.StartsWith("tags[", StringComparison.Ordinal));

    /// <summary>An Error per malformed tag, on <c>tags[i]</c>, and one on <c>tags</c> when there are too many.</summary>
    private IEnumerable<ValidationFinding> TagFindings(IReadOnlyList<string> tags)
    {
        if (tags.Count > DefinitionTagFormat.MaxCount)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"The definition has {tags.Count} tags; 'tags' allows at most {DefinitionTagFormat.MaxCount}.", FieldPath: "tags");
        }

        for (var i = 0; i < tags.Count; i++)
        {
            if (DefinitionTagFormat.Problem(tags[i]) is { } problem)
                yield return new ValidationFinding(RuleId, DefaultSeverity, $"Tag '{tags[i]}' {problem}.", FieldPath: $"tags[{i}]");
        }
    }

    private void AddIfBlank(List<ValidationFinding> findings, string? value, string fieldPath, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            findings.Add(new ValidationFinding(
                RuleId,
                DefaultSeverity,
                $"The definition needs {description}; '{fieldPath}' is missing or blank.",
                FieldPath: fieldPath));
        }
    }
}
