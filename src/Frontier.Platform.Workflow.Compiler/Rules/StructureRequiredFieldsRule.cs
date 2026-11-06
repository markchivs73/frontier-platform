namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.required-fields: the definition's name and engagement type are present and not blank,
/// each reported as an Error (S13.206, option C). Doc 13 §4.2, Phase 1 rule catalogue. A brand-new draft
/// starts with both blank, so its validation report fails and <c>ProposePublishAsync</c> refuses that report
/// until both are set.
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
        return findings;
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
