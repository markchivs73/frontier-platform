using Microsoft.DurableTask;
using Polly;

namespace Frontier.Platform.Resilience;

/// <summary>
/// The two-loop wiring point (doc 10 §1, §2, §5): <see cref="GetPipeline"/> builds the
/// inner Polly v8 pipeline an activity runs the provider call through;
/// <see cref="GetTaskOptions"/> builds the outer DTF retry handler an orchestrator
/// passes to <c>CallActivityAsync</c>. Both are pure functions of
/// <paramref name="profileName"/> over the compiled-in
/// <see cref="Phase1ResilienceProfileCatalogue"/> — safe to call from a DTF
/// orchestrator body (dtf-determinism: no I/O, same result every replay).
/// <see cref="ResiliencePipeline"/> (Polly) and <see cref="TaskOptions"/> (DTF) appear
/// directly on this interface because doc 10 §2 specifies these exact return types —
/// Resilience's entire purpose is to hand callers ready-to-use Polly/DTF policy
/// objects, not to wrap a platform-wide technology choice (TD-1) behind a swappable
/// abstraction.
/// </summary>
public interface IResiliencePolicyProvider
{
    /// <summary>The inner-loop Polly pipeline for <paramref name="profileName"/>, composed per doc 10 §4's order: <c>Timeout(total) → Bulkhead → CircuitBreaker → Retry → Timeout(per-attempt)</c>.</summary>
    ResiliencePipeline GetPipeline(string profileName);

    /// <summary>
    /// The inner-loop pipeline a call to <paramref name="provider"/>/<paramref name="modelId"/> runs
    /// through under <paramref name="profileName"/> (doc 10 §6): one pipeline per endpoint, kept for the
    /// worker's lifetime, so its circuit breaker (granularity (provider, modelId)) and bulkhead
    /// (granularity provider) see every call and <see cref="ICircuitStateProvider"/> reports its state.
    /// The default falls back to <see cref="GetPipeline(string)"/>, for implementations that key no endpoint.
    /// </summary>
    ResiliencePipeline GetPipeline(string profileName, string provider, string modelId) => GetPipeline(profileName);

    /// <summary>The outer-loop DTF retry handler for <paramref name="profileName"/>: <see cref="FailureClass.Permanent"/> failures (per <see cref="IFailureClassifier"/>) are never retried.</summary>
    TaskOptions GetTaskOptions(string profileName);
}
