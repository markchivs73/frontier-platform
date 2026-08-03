namespace Frontier.Platform.Resilience;

/// <summary>
/// Per-execution sliding-window state for <see cref="RetryBudget"/> (doc 10 §5):
/// "≤20% of invocations over the trailing 50 invocations, minimum floor of 10 retries
/// so small workflows aren't starved." Extracted from <see cref="RetryBudget"/> per
/// engineering-standards ("no private methods — extract helpers into testable
/// internal classes").
/// </summary>
internal sealed class RetryBudgetState
{
    /// <summary>Maximum entries retained in the sliding window (doc 10 §5 "trailing 50 invocations").</summary>
    internal const int WindowSize = 50;

    /// <summary>Minimum retries always allowed regardless of ratio (doc 10 §5 "floor of 10").</summary>
    internal const int MinimumFloor = 10;

    /// <summary>Maximum retry ratio over the window (doc 10 §5 "≤20%").</summary>
    internal const double RetryRatio = 0.20;

    private readonly Queue<RetryBudgetEntry> _window = new();
    private readonly object _lock = new();
    private int _invocationCount;
    private int _retryCount;

    /// <summary>
    /// Records one invocation attempt; returns <see langword="true"/> if within budget,
    /// <see langword="false"/> once the window's retry budget is exhausted.
    /// </summary>
    internal bool TryConsume()
    {
        lock (_lock)
        {
            SlideIfFull();
            _invocationCount++;
            var allowed = _retryCount < ComputeBudget(_invocationCount);
            if (allowed) _retryCount++;
            _window.Enqueue(new RetryBudgetEntry(allowed));
            return allowed;
        }
    }

    /// <summary>Returns the current window state for <see cref="RetryBudget.GetSnapshot"/>.</summary>
    internal (int InvocationCount, int RetryCount, bool IsExhausted) GetCounts()
    {
        lock (_lock)
        {
            var isExhausted = _invocationCount > 0 && _retryCount >= ComputeBudget(_invocationCount);
            return (_invocationCount, _retryCount, isExhausted);
        }
    }

    /// <summary>Calculates the allowed retry count for <paramref name="invocationCount"/> (doc 10 §5).</summary>
    internal static int ComputeBudget(int invocationCount) =>
        Math.Max(MinimumFloor, (int)(invocationCount * RetryRatio));

    /// <summary>Removes the oldest entry when the window is full, adjusting running counters.</summary>
    private void SlideIfFull()
    {
        if (_window.Count < WindowSize) return;
        var removed = _window.Dequeue();
        _invocationCount--;
        if (removed.WasAllowed) _retryCount--;
    }
}
