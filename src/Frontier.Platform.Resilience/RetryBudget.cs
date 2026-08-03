using System.Collections.Concurrent;

namespace Frontier.Platform.Resilience;

/// <summary>
/// <see cref="IRetryBudget"/> with per-execution sliding-window enforcement (doc 10
/// §5): "≤20% of invocations over the trailing 50 invocations, minimum floor of 10
/// retries so small workflows aren't starved." State is in-process per worker —
/// acceptable because instance affinity routes one execution's activities through few
/// workers (doc 10 §6). Per-execution state is held in <see cref="RetryBudgetState"/>;
/// the sliding-window arithmetic is in <see cref="RetryBudgetState.ComputeBudget"/>.
/// </summary>
internal sealed class RetryBudget : IRetryBudget
{
    private readonly ConcurrentDictionary<string, RetryBudgetState> _states = new();

    /// <inheritdoc />
    public bool TryConsume(string executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        return _states.GetOrAdd(executionId, static _ => new RetryBudgetState()).TryConsume();
    }

    /// <inheritdoc />
    public RetryBudgetSnapshot GetSnapshot(string executionId)
    {
        ArgumentNullException.ThrowIfNull(executionId);
        if (!_states.TryGetValue(executionId, out var state))
            return BuildSnapshot(executionId, 0, 0, false);
        var (invocationCount, retryCount, isExhausted) = state.GetCounts();
        return BuildSnapshot(executionId, invocationCount, retryCount, isExhausted);
    }

    private static RetryBudgetSnapshot BuildSnapshot(string executionId, int invocationCount, int retryCount, bool isExhausted) => new()
    {
        ExecutionId = executionId,
        InvocationCount = invocationCount,
        RetryCount = retryCount,
        IsExhausted = isExhausted,
    };
}
