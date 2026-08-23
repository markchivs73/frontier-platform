namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Contract for a validation rule. Rules are contributed by their owning libraries via DI (doc 12 §3);
/// the compiler aggregates them into one report consumed by canvas, propose, approve, and CI/CD paths.
/// Doc 13 §4.1–§4.2.
/// </summary>
public interface IDefinitionValidationRule
{
    /// <summary>
    /// Stable rule identifier: "structure.required-fields", "cascade.acyclic", etc.
    /// Used in reports, UI highlights, and severity overrides per deployment config.
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Execution tier: Pure (no I/O, runs per debounced edit in-circuit), Resourced (reads registries),
    /// or Runtime (executes—sandbox test-run, advisory only).
    /// </summary>
    RuleTier Tier { get; }

    /// <summary>
    /// Default severity: Error (blocks publish), Warning, or Info.
    /// Deployments can override per ADR-DC2; a Warning becoming Error is a governance decision.
    /// </summary>
    ValidationSeverity DefaultSeverity { get; }

    /// <summary>
    /// Evaluate the rule against the definition. Returns empty list if the rule passes.
    /// Pure rules execute synchronously; resourced rules may be async (registries can be slow).
    /// </summary>
    Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct);
}
