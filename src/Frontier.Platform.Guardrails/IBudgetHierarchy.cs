namespace Frontier.Platform.Guardrails;

/// <summary>
/// Hierarchical budget enforcement across invocation, engagement, and platform scopes
/// (doc 07 §4). Rolls up usage from lower tiers to check against parent budgets,
/// enabling multi-level admission control without double-counting.
/// </summary>
public interface IBudgetHierarchy
{
    /// <summary>
    /// Checks whether <paramref name="estimate"/> can be approved against the hierarchy
    /// of budgets: invocation scope, then engagement scope, then platform scope.
    /// Returns <c>true</c> if all applicable budgets have capacity.
    /// </summary>
    Task<bool> CanApproveAsync(BudgetScopeRef invocationScope, BudgetScopeRef engagementScope, InvocationCostEstimate estimate, CancellationToken cancellationToken);

    /// <summary>
    /// Records the actual usage across the hierarchy. Idempotent on correlation ID
    /// (invocation retries must not cause hierarchical double-counting).
    /// </summary>
    Task RecordHierarchicalUsageAsync(BudgetScopeRef invocationScope, BudgetScopeRef engagementScope, UsageRecord usage, CancellationToken cancellationToken);
}
