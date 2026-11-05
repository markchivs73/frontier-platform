using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Frontier.Platform.Abstractions;
using Microsoft.DurableTask;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Frontier.Platform.Resilience;

/// <summary>
/// <see cref="IResiliencePolicyProvider"/> over <see cref="Phase1ResilienceProfileCatalogue"/>
/// (doc 10 §4, §5). Inner pipelines compose, outermost first:
/// <c>Timeout(total) → Bulkhead → CircuitBreaker → Retry(classifier-gated, jittered) →
/// Timeout(per-attempt)</c> — retry sits inside the breaker so retries count toward its
/// failure ratio, and the bulkhead sits outside so saturation rejections don't trip it
/// (doc 10 §4). The outer DTF <see cref="RetryPolicy"/> uses deterministic exponential
/// backoff (no <see cref="Random"/> in an orchestrator body, dtf-determinism) and stops
/// retrying on the permanent exception types <see cref="ContractViolationException"/>,
/// <see cref="BudgetExceededException"/> and <see cref="GuardrailsSuspendedException"/> anywhere in the failure chain, via
/// <see cref="TaskFailureDetails.IsCausedBy{T}"/> — the classifications the outer loop
/// can enforce without a live exception instance (doc 10 §3, hard invariant 7).
/// Pipelines are built once and cached — per profile, and per <c>(profile, provider, modelId)</c>
/// endpoint — so breaker and bulkhead state accumulate across calls (doc 10 §6: in-process per worker).
/// </summary>
internal sealed class ResiliencePolicyProvider(IFailureClassifier classifier, CircuitStateProvider circuits, TimeProvider timeProvider) : IResiliencePolicyProvider
{
    /// <summary>Total pipeline timeout as a multiple of the per-attempt timeout (doc 10 §7: "total pipeline timeout (~3x attempt)").</summary>
    internal const int TotalTimeoutMultiplier = 3;

    private readonly ConcurrentDictionary<string, Lazy<ResiliencePipeline>> profilePipelines = new();
    private readonly ConcurrentDictionary<(string Profile, string Provider, string ModelId), Lazy<ResiliencePipeline>> endpointPipelines = new();
    private readonly ConcurrentDictionary<(string Profile, string Provider), Lazy<RateLimiter>> bulkheads = new();

    /// <summary>
    /// The pipeline for <paramref name="profileName"/> with no endpoint: built once and cached, so
    /// its breaker and bulkhead state accumulate across calls, but its breaker is not reported
    /// through <see cref="ICircuitStateProvider"/> because it is not keyed to a model.
    /// </summary>
    public ResiliencePipeline GetPipeline(string profileName)
    {
        var profile = ResolveProfile(profileName);
        return profilePipelines.GetOrAdd(profileName, _ => new(() => Compose(profile, NewBulkhead(profile.Bulkhead), BuildCircuitBreakerOptions(profile.CircuitBreaker)))).Value;
    }

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string profileName, string provider, string modelId)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(modelId);

        var profile = ResolveProfile(profileName);
        return endpointPipelines.GetOrAdd((profileName, provider, modelId), key => new(() => BuildEndpointPipeline(profile, key.Provider, key.ModelId))).Value;
    }

    /// <summary>
    /// Builds <paramref name="provider"/>/<paramref name="modelId"/>'s pipeline under <paramref name="profile"/>:
    /// its breaker is tracked by <see cref="CircuitStateProvider"/> (granularity (provider, modelId)), and
    /// its bulkhead is the one every model of <paramref name="provider"/> shares under that profile
    /// (granularity provider, doc 10 §6).
    /// </summary>
    internal ResiliencePipeline BuildEndpointPipeline(ResilienceProfile profile, string provider, string modelId)
    {
        var bulkhead = bulkheads.GetOrAdd((profile.ProfileId, provider), _ => new(() => NewBulkhead(profile.Bulkhead))).Value;
        var breaker = circuits.Track(provider, modelId, BuildCircuitBreakerOptions(profile.CircuitBreaker));
        return Compose(profile, bulkhead, breaker);
    }

    /// <summary>Composes doc 10 §4's order over the given bulkhead and breaker, timed by the provider's <see cref="TimeProvider"/>.</summary>
    internal ResiliencePipeline Compose(ResilienceProfile profile, RateLimiter bulkhead, CircuitBreakerStrategyOptions breaker) =>
        new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddTimeout(TimeSpan.FromMilliseconds(TotalTimeoutMs(profile)))
            .AddRateLimiter(bulkhead)
            .AddCircuitBreaker(breaker)
            .AddRetry(BuildRetryOptions(profile.InnerRetry, classifier))
            .AddTimeout(TimeSpan.FromMilliseconds(profile.TimeoutMs))
            .Build();

    /// <summary>The total pipeline timeout <see cref="Compose"/> applies: <see cref="TotalTimeoutMultiplier"/> per-attempt timeouts (doc 10 §7). <see cref="TimeoutHierarchyCheck"/> validates this value.</summary>
    internal static int TotalTimeoutMs(ResilienceProfile profile) => profile.TimeoutMs * TotalTimeoutMultiplier;

    /// <summary>A new bulkhead for <paramref name="spec"/>: at most <see cref="BulkheadSpec.MaxConcurrent"/> calls in flight, <see cref="BulkheadSpec.MaxQueue"/> waiting.</summary>
    internal static ConcurrencyLimiter NewBulkhead(BulkheadSpec spec) => new(new ConcurrencyLimiterOptions
    {
        PermitLimit = spec.MaxConcurrent,
        QueueLimit = spec.MaxQueue,
    });

    /// <inheritdoc />
    public TaskOptions GetTaskOptions(string profileName)
    {
        var profile = ResolveProfile(profileName);
        var retryPolicy = new RetryPolicy(
            maxNumberOfAttempts: profile.OuterRetry.MaxAttempts,
            firstRetryInterval: TimeSpan.FromMilliseconds(profile.InnerRetry.BaseDelayMs),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromMilliseconds(profile.InnerRetry.MaxDelayMs))
        {
            HandleFailure = HandleOuterRetryFailure,
        };

        return TaskOptions.FromRetryPolicy(retryPolicy);
    }

    /// <summary>Looks up <paramref name="profileName"/> in <see cref="Phase1ResilienceProfileCatalogue.ByProfileId"/>.</summary>
    internal static ResilienceProfile ResolveProfile(string profileName) =>
        Phase1ResilienceProfileCatalogue.ByProfileId.TryGetValue(profileName, out var profile)
            ? profile
            : throw new ArgumentOutOfRangeException(nameof(profileName), profileName, "Unknown resilience profile.");

    /// <summary>The outer DTF retry handler (doc 10 §3): every failure retries unless its chain carries a permanent cause (see <see cref="IsPermanentCause"/>).</summary>
    internal static bool HandleOuterRetryFailure(TaskFailureDetails failure) => !IsPermanentCause(failure);

    /// <summary>
    /// True when <paramref name="details"/> or any inner failure is a <see cref="ContractViolationException"/>,
    /// <see cref="BudgetExceededException"/> or <see cref="GuardrailsSuspendedException"/> — all <c>Permanent</c>
    /// in <see cref="FailureClassifier"/>.
    /// Each <see cref="TaskFailureDetails.IsCausedBy{T}"/> checks only its own level, so the chain is walked.
    /// </summary>
    internal static bool IsPermanentCause(TaskFailureDetails? details) => details switch
    {
        null => false,
        { } d when d.IsCausedBy<ContractViolationException>() || d.IsCausedBy<BudgetExceededException>() || d.IsCausedBy<GuardrailsSuspendedException>() => true,
        _ => IsPermanentCause(details.InnerFailure),
    };

    /// <summary>Builds the inner-loop retry options: decorrelated-jitter exponential backoff, gated by <see cref="IFailureClassifier"/> (doc 10 §3).</summary>
    internal static RetryStrategyOptions BuildRetryOptions(InnerRetrySpec spec, IFailureClassifier failureClassifier) => new()
    {
        MaxRetryAttempts = spec.MaxAttempts - 1,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(spec.BaseDelayMs),
        MaxDelay = TimeSpan.FromMilliseconds(spec.MaxDelayMs),
        ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is { } exception && failureClassifier.Classify(exception).Class.IsRetryable),
    };

    /// <summary>Builds the circuit breaker options for a profile's <see cref="CircuitBreakerSpec"/> (doc 10 §6: granularity (provider, modelId)).</summary>
    internal static CircuitBreakerStrategyOptions BuildCircuitBreakerOptions(CircuitBreakerSpec spec) => new()
    {
        FailureRatio = spec.FailureRatio,
        MinimumThroughput = spec.MinThroughput,
        SamplingDuration = TimeSpan.FromSeconds(spec.SamplingWindowSeconds),
        BreakDuration = TimeSpan.FromSeconds(spec.BreakDurationSeconds),
    };
}
