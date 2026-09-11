using System.Net;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// Classifies a Cosmos ledger write failure (doc 07 §6): whether it lost an optimistic-concurrency race and the
/// read-accumulate-write should start again, and how long to back off first.
/// </summary>
internal static class BudgetLedgerWriteConflict
{
    /// <summary>The backoff ceiling after the first lost attempt, in milliseconds; it doubles with each further loss.</summary>
    internal const int BaseBackoffMilliseconds = 25;

    /// <summary>The largest backoff ceiling, in milliseconds, however many attempts have been lost.</summary>
    internal const int MaxBackoffMilliseconds = 1000;

    /// <summary>
    /// The upper bound of the full-jitter delay before the next attempt: exponential in the attempts already lost
    /// (25, 50, 100 … ms) and capped, so a crowd of contenders spreads out quickly without one waiting unboundedly.
    /// </summary>
    internal static TimeSpan BackoffCeiling(int failedAttempts)
    {
        var exponent = Math.Clamp(failedAttempts - 1, 0, 30);
        return TimeSpan.FromMilliseconds(Math.Min((long)BaseBackoffMilliseconds << exponent, MaxBackoffMilliseconds));
    }

    /// <summary>
    /// <c>true</c> for 412 PreconditionFailed (a replace whose ETag went stale) and 409 Conflict (a first create another
    /// writer beat); every other status is not a concurrency conflict.
    /// </summary>
    internal static bool IsConcurrencyConflict(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
}
