using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The CEL port (ADR-E27 D1, TD-17). The one adapter is <see cref="CellyRuleEngine"/>; nothing
/// outside it and its helpers sees a Celly type.
/// </summary>
internal interface IRuleEngine
{
    /// <summary>
    /// Compiles <paramref name="rules"/> against the types <paramref name="schema"/> declares. A rule
    /// whose cost the schema does not bound is always refused; one estimated over
    /// <paramref name="maxRuleCost"/> is refused only when a ceiling is given, which registration does
    /// and the validator does not (ADR-E27 D2, ADR-PA43).
    /// </summary>
    RuleCompilation Compile(JsonElement schema, IReadOnlyList<ContractRule> rules, int? maxRuleCost);
}

/// <summary>
/// The outcome of compiling a contract's rules: <see cref="Rules"/> when every rule compiled,
/// otherwise the findings saying why not.
/// </summary>
internal sealed record RuleCompilation(ICompiledRules? Rules, IReadOnlyList<ContractFinding> Findings);

/// <summary>A contract's compiled rules. Thread-safe.</summary>
internal interface ICompiledRules
{
    /// <summary>
    /// The violations of <paramref name="payload"/>, which must already satisfy the schema, at most
    /// <paramref name="maxViolations"/>; empty when every rule holds. Each rule may take
    /// <paramref name="maxIterations"/> comprehension iterations. Both are the caller's budgets, so
    /// one compilation serves every budget (ADR-PA43).
    /// </summary>
    IReadOnlyList<ContractViolation> Evaluate(JsonElement payload, int maxViolations, int maxIterations);
}
