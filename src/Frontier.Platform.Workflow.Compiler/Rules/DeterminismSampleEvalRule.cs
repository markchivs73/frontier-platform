namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// determinism.sample-eval (doc 13 §4.2 R4, S9.30 — Runtime tier, Info): where the designer
/// supplies sample data, predicate trees are evaluated against it ("with budget = 1200 this
/// routes to the Business gate"). Runtime-tier rules execute in the sandbox test-run channel,
/// not in <c>ValidateAsync</c> (the validator filters to Pure + Resourced), and Phase 1 has no
/// designer sample-data channel yet — this registration is the catalogue row's placeholder;
/// execution wiring lands with S9.38's real sandbox test-run (see docs/state/SPEC-TRACEABILITY.md).
/// </summary>
public sealed class DeterminismSampleEvalRule : IDefinitionValidationRule
{
    public string RuleId => "determinism.sample-eval";
    public RuleTier Tier => RuleTier.Runtime;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // No sample data channel exists in Phase 1: the rule's trigger condition is absent by
        // construction, not ignored — S9.38 supplies the channel and the evaluation.
        return Task.FromResult<IReadOnlyList<ValidationFinding>>([]);
    }
}
