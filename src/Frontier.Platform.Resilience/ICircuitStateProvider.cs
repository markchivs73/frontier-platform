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

    /// <summary>Registers a callback invoked on every <see cref="CircuitTransition"/>; dispose to unsubscribe.</summary>
    IDisposable Subscribe(Action<CircuitTransition> onTransition);
}
