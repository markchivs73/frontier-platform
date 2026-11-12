namespace Frontier.Platform.Resilience;

/// <summary>
/// Per-execution sliding-window amplification guard (doc 10 §2, §5): bounds how many
/// retries one execution can spend, independent of any single profile's own
/// <c>maxAttempts</c>. Exhaustion converts a would-be retry into immediate escalation
/// (the run ends <c>failed</c> with <c>retry_budget_exhausted</c>; ADR-PA47).
/// </summary>
public interface IRetryBudget
{
    /// <summary>Records one retry attempt for <paramref name="executionId"/>; returns <c>false</c> once the window's budget is exhausted.</summary>
    bool TryConsume(string executionId);

    /// <summary>The current window state for <paramref name="executionId"/>.</summary>
    RetryBudgetSnapshot GetSnapshot(string executionId);
}
