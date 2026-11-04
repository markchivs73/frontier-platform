using System.Collections.Concurrent;
using System.Collections.Immutable;
using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience;

/// <summary>
/// In-process <see cref="ICircuitStateProvider"/> (doc 10 §6: "Phase 1: breaker/bulkhead state is
/// in-process per worker"). <see cref="ResiliencePolicyProvider"/> registers every endpoint
/// pipeline's breaker here through <see cref="Track"/>, so <see cref="GetState"/> reports the
/// breaker the model calls actually run through, and every transition is published to
/// <see cref="Subscribe"/>rs. A <c>(provider, modelId)</c> pair called under more than one profile
/// has one breaker per profile; <see cref="GetState"/> reports the most severe of them. A pair no
/// call has reached yet has no breaker and reports <see cref="CircuitState.Closed"/>.
/// </summary>
internal sealed class CircuitStateProvider(TimeProvider timeProvider) : ICircuitStateProvider
{
    private readonly ConcurrentDictionary<(string Provider, string ModelId), ImmutableList<TrackedBreaker>> breakers = new();
    private ImmutableList<Action<CircuitTransition>> subscribers = [];

    /// <inheritdoc />
    public CircuitState GetState(string provider, string modelId) =>
        breakers.TryGetValue((provider, modelId), out var tracked)
            ? MostSevere(tracked.Select(breaker => breaker.StateProvider.CircuitState))
            : CircuitState.Closed;

    /// <inheritdoc />
    public IDisposable Subscribe(Action<CircuitTransition> onTransition)
    {
        ArgumentNullException.ThrowIfNull(onTransition);
        ImmutableInterlocked.Update(ref subscribers, list => list.Add(onTransition));
        return new Subscription(() => ImmutableInterlocked.Update(ref subscribers, list => list.Remove(onTransition)));
    }

    /// <summary>
    /// Attaches a state provider and transition callbacks to <paramref name="options"/> and registers
    /// the breaker under <paramref name="provider"/>/<paramref name="modelId"/>. Returns the same options.
    /// </summary>
    internal CircuitBreakerStrategyOptions Track(string provider, string modelId, CircuitBreakerStrategyOptions options)
    {
        var tracked = new TrackedBreaker(new CircuitBreakerStateProvider());
        options.StateProvider = tracked.StateProvider;
        options.OnOpened = args => Transition(tracked, provider, modelId, args.IsManual ? CircuitState.Isolated : CircuitState.Open);
        options.OnHalfOpened = _ => Transition(tracked, provider, modelId, CircuitState.HalfOpen);
        options.OnClosed = _ => Transition(tracked, provider, modelId, CircuitState.Closed);
        breakers.AddOrUpdate((provider, modelId), _ => [tracked], (_, list) => list.Add(tracked));
        return options;
    }

    /// <summary>Records <paramref name="tracked"/>'s move to <paramref name="to"/> and publishes it to every subscriber.</summary>
    internal ValueTask Transition(TrackedBreaker tracked, string provider, string modelId, CircuitState to)
    {
        var transition = new CircuitTransition
        {
            Provider = provider,
            ModelId = modelId,
            From = tracked.Exchange(to),
            To = to,
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };

        foreach (var subscriber in subscribers)
        {
            subscriber(transition);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>The most severe of <paramref name="states"/>: isolated, then open, then half-open, then closed.</summary>
    internal static CircuitState MostSevere(IEnumerable<CircuitState> states) =>
        states.MaxBy(Severity);

    /// <summary>Ranks a state for <see cref="MostSevere"/>; a breaker that lets no traffic through ranks above one that probes.</summary>
    internal static int Severity(CircuitState state) => state switch
    {
        CircuitState.Isolated => 3,
        CircuitState.Open => 2,
        CircuitState.HalfOpen => 1,
        _ => 0,
    };

    /// <summary>One registered breaker: Polly's live state, plus the last state it published, for <see cref="CircuitTransition.From"/>.</summary>
    internal sealed class TrackedBreaker(CircuitBreakerStateProvider stateProvider)
    {
        private int last = (int)CircuitState.Closed;

        /// <summary>Polly's view of the breaker's current state.</summary>
        public CircuitBreakerStateProvider StateProvider { get; } = stateProvider;

        /// <summary>Records <paramref name="to"/> as the last published state and returns the one it replaces.</summary>
        public CircuitState Exchange(CircuitState to) => (CircuitState)Interlocked.Exchange(ref last, (int)to);
    }

    /// <summary>The handle returned by <see cref="Subscribe"/>; disposing it removes the callback.</summary>
    internal sealed class Subscription(Action unsubscribe) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => unsubscribe();
    }
}
