namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Base for pure-tier rules (no I/O, synchronous). Subclasses implement ValidateStructural;
/// EvaluateAsync wraps it in a completed task so rules are polymorphic over tier.
/// </summary>
public abstract class PureTierRule : IDefinitionValidationRule
{
    public abstract string RuleId { get; }
    public RuleTier Tier => RuleTier.Pure;
    public abstract ValidationSeverity DefaultSeverity { get; }

    protected abstract IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx);

    public Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        var findings = ValidateStructural(ctx);
        return Task.FromResult(findings);
    }
}
