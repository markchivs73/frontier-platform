using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Whether one breaker lets a call through now (S13.231, doc 10 §6), the decision behind
/// <see cref="ICircuitStateProvider.AdmitsCalls"/>.
/// </summary>
internal static class CircuitAdmission
{
    /// <summary>
    /// Closed admits. Open admits only once its break has ended (that call is the probe), and only when the open
    /// has been published: Polly sets Open before it runs <c>OnOpened</c> on a background task, and until then the
    /// recorded break end belongs to an earlier break, or to none. Half-open (a probe is running) and isolated never admit.
    /// </summary>
    /// <param name="polly">Polly's live state.</param>
    /// <param name="published">The last state this breaker's callbacks published.</param>
    /// <param name="breakEndsAt">The end of the break recorded by the last published open.</param>
    /// <param name="now">The current time.</param>
    internal static bool Admits(CircuitState polly, CircuitState published, DateTimeOffset breakEndsAt, DateTimeOffset now) => polly switch
    {
        CircuitState.Closed => true,
        CircuitState.Open => published == CircuitState.Open && now >= breakEndsAt,
        _ => false,
    };
}
