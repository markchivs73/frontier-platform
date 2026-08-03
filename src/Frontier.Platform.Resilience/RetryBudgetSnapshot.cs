using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The sliding-window retry-amplification state for one execution (doc 10 §5: "retries
/// ≤ 20% of invocations over the trailing 50 invocations, minimum floor of 10
/// retries").
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by RetryBudget tests.")]
public sealed record RetryBudgetSnapshot
{
    /// <summary>The execution this snapshot describes.</summary>
    public required string ExecutionId { get; init; }

    /// <summary>The number of activity invocations counted in the current window.</summary>
    public required int InvocationCount { get; init; }

    /// <summary>The number of retries counted in the current window.</summary>
    public required int RetryCount { get; init; }

    /// <summary>Whether the window has exhausted its retry budget — the honest "environment is degraded" signal (doc 10 §5).</summary>
    public required bool IsExhausted { get; init; }
}
