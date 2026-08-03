using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience;

/// <summary>
/// Phase 1 stub <see cref="ICircuitStateProvider"/> (doc 10 §6: "Phase 1: breaker/bulkhead
/// state is in-process per worker"). <see cref="ResiliencePolicyProvider"/>'s per-profile
/// pipelines do not yet track per-<c>(provider, modelId)</c> breaker state or publish
/// transitions — <see cref="GetState"/> always reports <see cref="CircuitState.Closed"/>
/// and <see cref="Subscribe"/> never invokes its callback. <see cref="ModelResolver"/>
/// (S4.3) does not consume this yet either; both are wired together once a real
/// provider call exists to break (S4.2).
/// </summary>
internal sealed class CircuitStateProvider : ICircuitStateProvider
{
    /// <inheritdoc />
    public CircuitState GetState(string provider, string modelId) => CircuitState.Closed;

    /// <inheritdoc />
    public IDisposable Subscribe(Action<CircuitTransition> onTransition) => new NoOpSubscription();

    /// <summary>The no-op handle returned by <see cref="Subscribe"/>.</summary>
    internal sealed class NoOpSubscription : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
