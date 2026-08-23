using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// context.baseline-scoped (doc 13 §4.2 R2, master design §2.2, doc 04): baseline context
/// requests name specific components — whole-store requests (a <c>"*"</c> wildcard or a blank
/// entry) are forbidden, so an agent can never pull the entire baseline catalogue.
/// </summary>
public sealed class ContextBaselineScopedRule : PureTierRule
{
    public override string RuleId => "context.baseline-scoped";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes.OfType<AgentTaskNode>()
            .SelectMany(node => node.ContextRequest.BaselineComponents
                .Where(component => string.IsNullOrWhiteSpace(component) || component == "*")
                .Select(component => new ValidationFinding(RuleId, DefaultSeverity,
                    $"baseline_components entry '{component}' is not component-scoped — name a specific baseline component.",
                    node.NodeId, FieldPath: "context_request.baseline_components")))
            .ToList();
}
