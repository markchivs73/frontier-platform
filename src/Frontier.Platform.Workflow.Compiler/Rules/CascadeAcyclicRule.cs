namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// cascade.acyclic (doc 13 §4.2 R3, doc 03 §2): the section dependency graph derived from Data
/// edges has no cycles and no dangling section refs. Delegates to Cascade Logic's compile-time
/// guardian — the rule's owning library per the doc-13 ownership matrix — through the
/// consumer-owned <see cref="ICascadeGraphChecker"/> seam (Host adapts
/// <c>ICascadeGraphValidator.ValidateAtPublish</c>). Data-edge contract-type <b>matching</b> is
/// <b>not</b> covered here — that is owned solely by the anchored <c>data.edge-type-match</c>
/// rule (S9.70).
/// </summary>
public sealed class CascadeAcyclicRule : PureTierRule
{
    private readonly ICascadeGraphChecker _cascadeChecker;

    /// <summary>Constructs the rule over Cascade Logic's publish-time checker seam.</summary>
    public CascadeAcyclicRule(ICascadeGraphChecker cascadeChecker)
    {
        ArgumentNullException.ThrowIfNull(cascadeChecker);
        _cascadeChecker = cascadeChecker;
    }

    public override string RuleId => "cascade.acyclic";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        _cascadeChecker.CheckAtPublish(ctx.Definition)
            .Select(violation => new ValidationFinding(RuleId, DefaultSeverity, violation, SourceLibrary: "cascade-logic"))
            .ToList();
}
