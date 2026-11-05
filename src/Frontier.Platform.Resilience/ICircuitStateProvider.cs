using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Breaker state for a <c>(provider, modelId)</c> pair (doc 10 §2, §6), consumed by
/// Model-Role Config's chain walk (doc 08 §5: <see cref="CircuitState.Open"/> means
/// "skip to fallback") and by Observability/alerting via <see cref="Subscribe"/>.
/// </summary>
public interface ICircuitStateProvider
{
    /// <summary>The current breaker state for <paramref name="provider"/>/<paramref name="modelId"/>.</summary>
    CircuitState GetState(string provider, string modelId);

    /// <summary>
    /// Whether a call routed to <paramref name="provider"/>/<paramref name="modelId"/> now would be let through: the
    /// question the chain walk asks (doc 08 §5, doc 10 §6). It differs from <see cref="GetState"/> once an open
    /// breaker's break has elapsed. Polly still reports <see cref="CircuitState.Open"/> until a call arrives, but that
    /// call is the half-open probe, so the pair admits it. While the probe runs (<see cref="CircuitState.HalfOpen"/>),
    /// the pair admits nothing more. The default, for implementations that cannot see break timing, admits only
    /// <see cref="CircuitState.Closed"/> and <see cref="CircuitState.HalfOpen"/>.
    /// </summary>
    bool AdmitsCalls(string provider, string modelId) =>
        GetState(provider, modelId) is CircuitState.Closed or CircuitState.HalfOpen;

    /// <summary>Registers a callback invoked on every <see cref="CircuitTransition"/>; dispose to unsubscribe.</summary>
    IDisposable Subscribe(Action<CircuitTransition> onTransition);
}
