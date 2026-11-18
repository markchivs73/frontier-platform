using Polly;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Which outcomes count toward a node profile's breaker (doc 10 §4, §6). Polly's default — every exception but a
/// cancellation — with one exception: a network policy refusal (<see cref="PolicyRefusal"/>) never reached the server,
/// so it is not the server's failure and does not count (ADR-PA48, amended 2026-11-17). Every other exception the call
/// throws still counts, permanent ones included.
/// </summary>
internal static class NodeBreakerOutcome
{
    /// <summary>Whether <paramref name="outcome"/> counts toward the breaker.</summary>
    internal static bool IsFailure(Outcome<object> outcome) => outcome.Exception switch
    {
        null or OperationCanceledException => false,
        { } exception => !PolicyRefusal.IsRefusal(exception),
    };
}
