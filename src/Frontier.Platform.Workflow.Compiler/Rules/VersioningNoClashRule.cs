namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// versioning.no-clash (doc 13 §4.2 R6, §4.3): the concurrent-edit mechanics — a save/submit
/// carrying a stale <c>draftRevision</c> → structured 409 — are enforced at the save path
/// (<c>DefinitionLifecycleService.SaveDraftAsync</c>'s ETag guard), which a definition-shaped rule
/// cannot see. This rule validates the envelope's version integrity.
/// <para>
/// <c>definition_version</c> is <b>system-assigned at publish</b>, never by the designer or the
/// agent: a brand-new workflow's draft is minted at version <c>0</c> (the unversioned sentinel,
/// <c>DefinitionLifecycleService.CreateDraftAsync</c>), and <c>PublishVersionAsync</c> stamps the
/// real monotonic number (<c>NextVersionNumber</c>), which the store then projects back onto the
/// loaded definition (<c>CosmosDefinitionStore</c>). So this rule runs entirely in the design/draft
/// phase, where <c>0</c> is the correct, unresolvable-by-the-user pre-publish state — it must accept
/// it (rejecting it would block test-runs and the S9.73 agent-repair loop on every from-scratch
/// workflow, doc 13 §5). Only a genuinely-corrupt <b>negative</b> version is a finding.
/// </para>
/// </summary>
public sealed class VersioningNoClashRule : PureTierRule
{
    public override string RuleId => "versioning.no-clash";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.DefinitionVersion >= 0
            ? []
            : [new ValidationFinding(RuleId, DefaultSeverity,
                "definition_version must not be negative.", FieldPath: "definition_version")];
}
