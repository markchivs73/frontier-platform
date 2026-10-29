using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The CEL port (ADR-E27 D1, TD-17). The one adapter is <see cref="CellyRuleEngine"/>; nothing
/// outside it and its helpers sees a Celly type.
/// </summary>
internal interface IRuleEngine
{
    /// <summary>Compiles <paramref name="rules"/> against the types <paramref name="schema"/> declares.</summary>
    RuleCompilation Compile(JsonElement schema, IReadOnlyList<ContractRule> rules);
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
    /// <paramref name="maxViolations"/>; empty when every rule holds.
    /// </summary>
    IReadOnlyList<ContractViolation> Evaluate(JsonElement payload, int maxViolations);
}
