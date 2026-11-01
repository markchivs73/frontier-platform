using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>The budgets <see cref="ContractsOptions"/> configures: what a call that passes no <see cref="ContractBudgets"/> uses.</summary>
internal static class ConfiguredBudgets
{
    /// <summary>The four budgets <paramref name="options"/> holds, as a record.</summary>
    internal static ContractBudgets From(ContractsOptions options) => new()
    {
        MaxRuleCost = options.MaxRuleCost,
        MaxRuleIterations = options.MaxRuleIterations,
        MaxPayloadDepth = options.MaxPayloadDepth,
        MaxViolations = options.MaxViolations,
    };

    /// <summary>Refuses a missing record, or one out of range, before it is used.</summary>
    /// <exception cref="ContractViolationException"><paramref name="budgets"/> is out of range.</exception>
    internal static void EnsureValid(ContractBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        budgets.Validate();
    }
}
