using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// context.known-components (doc 13 §4.2 R2, doc 04 §10, S9.30): every requested baseline
/// component must exist in the baseline catalogue, and every requested dynamic field must be a
/// known engagement context field (hit for real at S9.28, where <c>engagement_brief</c> had to
/// exist for the helpdesk run). Whole-store entries are <c>context.baseline-scoped</c>'s
/// findings, not duplicated here.
/// </summary>
public sealed class ContextKnownComponentsRule : IDefinitionValidationRule
{
    private readonly IContextComponentCatalog _catalog;

    /// <summary>Constructs the rule over the context component catalogue.</summary>
    public ContextKnownComponentsRule(IContextComponentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public string RuleId => "context.known-components";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var baseline = (await _catalog.GetBaselineComponentNamesAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var dynamicFields = (await _catalog.GetDynamicFieldNamesAsync(ct)).ToHashSet(StringComparer.Ordinal);

        return ctx.Definition.Nodes.OfType<AgentTaskNode>()
            .SelectMany(node => NodeFindings(node, baseline, dynamicFields))
            .ToList();
    }

    private IEnumerable<ValidationFinding> NodeFindings(
        AgentTaskNode node, HashSet<string> baseline, HashSet<string> dynamicFields)
    {
        foreach (var component in node.ContextRequest.BaselineComponents
                     .Where(c => !string.IsNullOrWhiteSpace(c) && c != "*" && !baseline.Contains(c)))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"baseline_component '{component}' does not exist in the baseline catalogue.",
                node.NodeId, FieldPath: "context_request.baseline_components");
        }

        foreach (var field in node.ContextRequest.DynamicFields.Where(f => !dynamicFields.Contains(f)))
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"dynamic_field '{field}' is not a known engagement context field.",
                node.NodeId, FieldPath: "context_request.dynamic_fields");
        }
    }
}
