namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.required-fields: Name, description, engagement type present; tags well-formed (if present).
/// Doc 13 §4.2, Phase 1 rule catalogue.
/// </summary>
public sealed class StructureRequiredFieldsRule : PureTierRule
{
    public override string RuleId => "structure.required-fields";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var findings = new List<ValidationFinding>();
        // Phase 1: Placeholder. Detailed validation deferred pending contract finalization.
        return findings;
    }
}
