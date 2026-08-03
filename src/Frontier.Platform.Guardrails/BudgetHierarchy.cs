namespace Frontier.Platform.Guardrails;

/// <summary>
/// In-memory <see cref="IBudgetHierarchy"/> implementation for PoC (S6.5).
/// Checks budgets at invocation, execution, and engagement scopes against
/// <see cref="GuardrailPolicy.PerInvocation"/>, <see cref="GuardrailPolicy.PerExecution"/>,
/// and <see cref="GuardrailPolicy.PerEngagement"/> respectively.
/// Fleet-level budget is alert-only (doc 07 §6) and is never an admission scope.
/// </summary>
internal sealed class BudgetHierarchy(IBudgetLedger ledger, GuardrailPolicy policy) : IBudgetHierarchy
{
    /// <inheritdoc />
    public async Task<bool> CanApproveAsync(BudgetScopeRef invocationScope, BudgetScopeRef engagementScope, InvocationCostEstimate estimate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocationScope);
        ArgumentNullException.ThrowIfNull(engagementScope);
        ArgumentNullException.ThrowIfNull(estimate);

        if (!await CanApproveAtScopeAsync(invocationScope, policy.PerInvocation, estimate, cancellationToken))
            return false;

        var executionScope = new BudgetScopeRef(BudgetScopeKind.Execution, estimate.ExecutionId);
        if (!await CanApproveAtScopeAsync(executionScope, policy.PerExecution, estimate, cancellationToken))
            return false;

        return await CanApproveAtScopeAsync(engagementScope, policy.PerEngagement, estimate, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordHierarchicalUsageAsync(BudgetScopeRef invocationScope, BudgetScopeRef engagementScope, UsageRecord usage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocationScope);
        ArgumentNullException.ThrowIfNull(engagementScope);
        ArgumentNullException.ThrowIfNull(usage);

        // The ledger aggregates by scope kind at query time (BudgetLedger.Matches);
        // one record is sufficient for all hierarchy levels — recording per scope would double-count.
        return ledger.RecordUsageAsync(usage, cancellationToken);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="budget"/> is <c>null</c> (unbounded at this scope)
    /// or the ledger snapshot for <paramref name="scope"/> has capacity for <paramref name="estimate"/>.
    /// </summary>
    internal async Task<bool> CanApproveAtScopeAsync(BudgetScopeRef scope, BudgetSpec? budget, InvocationCostEstimate estimate, CancellationToken cancellationToken)
    {
        if (budget is null)
            return true;

        var snapshot = await ledger.GetSnapshotAsync(scope, cancellationToken);
        return BudgetHasCapacity(snapshot, budget, estimate);
    }

    /// <summary>
    /// Returns <c>false</c> if any field of <paramref name="budget"/> would be breached by
    /// adding <paramref name="estimate"/> on top of <paramref name="snapshot"/>'s current usage.
    /// Uses <c>PromptTokens + MaxOutputTokens</c> as a worst-case token estimate (doc 07 §4).
    /// </summary>
    internal static bool BudgetHasCapacity(BudgetSnapshot snapshot, BudgetSpec budget, InvocationCostEstimate estimate)
    {
        if (budget.MaxTokens.HasValue && snapshot.TokensUsed + estimate.PromptTokens + estimate.MaxOutputTokens > budget.MaxTokens.Value)
            return false;

        if (budget.MaxCostGbp.HasValue && snapshot.CostGbp + estimate.EstimatedCostGbp > budget.MaxCostGbp.Value)
            return false;

        if (budget.MaxAgentInvocations.HasValue && snapshot.InvocationCount + 1 > budget.MaxAgentInvocations.Value)
            return false;

        return true;
    }
}
