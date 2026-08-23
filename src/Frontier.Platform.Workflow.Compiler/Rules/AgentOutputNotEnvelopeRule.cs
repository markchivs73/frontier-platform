
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// agent.output-not-envelope (doc 13 §4.2, S13.7d; ADR-E2 deferral (c)): an
/// <see cref="AgentTaskNode"/> may not declare <see cref="TypedPayload"/> as its output
/// contract until the ADR-AG1 schema-validated variant lands — the envelope's free-form
/// payload has no honest CLR-derived structured-output schema, so the runtime's
/// <c>CanonicalOutputSchema</c> refuses it and the run would fail permanently. This rule
/// makes that refusal a design-time finding instead (the ADR-DC7 posture).
/// </summary>
public sealed class AgentOutputNotEnvelopeRule : PureTierRule
{
    public override string RuleId => "agent.output-not-envelope";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Nodes
            .OfType<AgentTaskNode>()
            .Where(node => string.Equals(node.OutputContractType, nameof(TypedPayload), StringComparison.Ordinal))
            .Select(node => new ValidationFinding(RuleId, DefaultSeverity,
                $"'{nameof(TypedPayload)}' cannot be an agent output contract until the ADR-AG1 schema-validated variant lands (ADR-E2 deferral (c)) — the envelope's free-form payload has no structured-output schema, so the run would fail permanently. Declare the concrete section contract instead.",
                node.NodeId, FieldPath: "output_contract_type"))
            .ToList();
}
