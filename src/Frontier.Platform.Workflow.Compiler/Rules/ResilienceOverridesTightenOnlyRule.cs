
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// resilience.overrides-tighten-only (doc 13 §4.2 R2, doc 10 §4): the structural half — retry
/// overrides, when present, must be positive. The cap-vs-profile comparison (an override may
/// never exceed the named profile's own cap) needs the profile catalogue, so it lives with the
/// Resourced <c>resilience.profile-exists</c> rule per the S9.30 split decision
/// (DESIGN-DECISIONS.md).
/// </summary>
public sealed class ResilienceOverridesTightenOnlyRule : PureTierRule
{
    public override string RuleId => "resilience.overrides-tighten-only";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes
            .Where(node => node.Retry is not null)
            .SelectMany(NodeFindings)
            .ToList();

    private IEnumerable<ValidationFinding> NodeFindings(WorkflowNode node)
    {
        if (node.Retry!.MaxAttemptsOverride is < 1)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                "max_attempts_override must be a positive attempt cap.",
                node.NodeId, FieldPath: "retry.max_attempts_override");
        }

        if (node.Retry.TimeoutSecondsOverride is < 1)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                "timeout_seconds_override must be a positive per-attempt timeout.",
                node.NodeId, FieldPath: "retry.timeout_seconds_override");
        }
    }
}
