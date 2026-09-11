using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// ADR-PA21: every cost amount carries an ISO 4217 currency code, and no conversion exists, so
/// two amounts may only be combined when their codes match. A mismatch is a
/// <see cref="ContractViolationException"/> — permanent, never retried — because retrying cannot
/// make a GBP budget comparable with a USD estimate.
/// </summary>
internal static class CostCurrency
{
    /// <summary>
    /// Throws <see cref="ContractViolationException"/> for <paramref name="contractType"/> unless
    /// <paramref name="combined"/> is <c>null</c> (nothing accumulated yet) or ordinally equals
    /// <paramref name="incoming"/>.
    /// </summary>
    internal static void EnsureCombinable(string contractType, string? combined, string? incoming)
    {
        if (combined is null || string.Equals(combined, incoming, StringComparison.Ordinal))
            return;

        throw new ContractViolationException(
            contractType,
            [$"cost currency mismatch: cannot combine '{combined}' with '{incoming ?? "(none)"}'; no currency conversion exists (ADR-PA21)."]);
    }

    /// <summary>
    /// Throws <see cref="ContractViolationException"/> when <paramref name="budget"/> declares a
    /// cost ceiling whose currency is not <paramref name="estimate"/>'s — a ceiling without a
    /// currency is refused too, since it cannot be compared with anything.
    /// </summary>
    internal static void EnsureComparable(BudgetSpec budget, InvocationCostEstimate estimate)
    {
        if (budget.MaxCost.HasValue && !string.Equals(budget.Currency, estimate.Currency, StringComparison.Ordinal))
            EnsureCombinable(nameof(BudgetSpec), budget.Currency ?? "(none)", estimate.Currency);
    }
}
