using Celly.Values;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// What one rule's result means (ADR-E27 D2): true passes; false, an error and an exhausted budget
/// are all permanent violations. The message is the rule's own, so it quotes the contract and never
/// the payload; an evaluation error's text is not passed on, since it can.
/// </summary>
internal static class RuleOutcome
{
    /// <summary>The violation <paramref name="result"/> means for <paramref name="rule"/>, or null when it holds.</summary>
    internal static ContractViolation? Violation(ContractRule rule, CelValue result)
    {
        var location = rule.FieldPath ?? string.Empty;
        return result switch
        {
            BoolValue { Value: true } => null,
            BoolValue => new ContractViolation(location, "rule", SchemaViolations.Bound(rule.Message)),
            ErrorValue error when error.Message.Contains("budget", StringComparison.Ordinal) =>
                new ContractViolation(location, "rule", SchemaViolations.Bound($"{rule.Message} (the rule ran past its evaluation budget)")),
            _ => new ContractViolation(location, "rule", SchemaViolations.Bound($"{rule.Message} (the rule could not be evaluated)")),
        };
    }
}
