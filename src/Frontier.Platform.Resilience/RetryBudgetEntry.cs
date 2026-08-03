using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>
/// One event in the per-execution <see cref="RetryBudgetState"/> sliding window (doc 10
/// §5): records whether the corresponding <see cref="IRetryBudget.TryConsume"/> call
/// was within budget (<see cref="WasAllowed"/> = <see langword="true"/>) or exhausted it.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by RetryBudgetState tests.")]
internal record struct RetryBudgetEntry(bool WasAllowed);
