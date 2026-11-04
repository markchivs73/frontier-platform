using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// context.baseline-scoped (doc 13 §4.2 R2, master design §2.2, doc 04): baseline context
/// requests name specific components — whole-store requests (a <c>"*"</c> wildcard or a blank
/// entry) are forbidden, so an agent can never pull the entire baseline catalogue. An empty list
/// is refused too: <see cref="ContextRequest.Validate"/> rejects it at run time, so a definition
/// carrying one must not publish clean (ADR-DC7).
/// </summary>
public sealed class ContextBaselineScopedRule : PureTierRule
{
    private const string FieldPath = "context_request.baseline_components";

    public override string RuleId => "context.baseline-scoped";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes.OfType<AgentTaskNode>().SelectMany(Evaluate).ToList();

    private IEnumerable<ValidationFinding> Evaluate(AgentTaskNode node)
    {
        var components = node.ContextRequest.BaselineComponents;
        if (components.Count == 0)
        {
            return [Finding(node, "baseline_components is empty — name at least one specific baseline component.")];
        }

        return components
            .Where(component => string.IsNullOrWhiteSpace(component) || component == "*")
            .Select(component => Finding(node,
                $"baseline_components entry '{component}' is not component-scoped — name a specific baseline component."));
    }

    private ValidationFinding Finding(AgentTaskNode node, string message) =>
        new(RuleId, DefaultSeverity, message, node.NodeId, FieldPath: FieldPath);
}
