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
/// retrying on the permanent exception types <see cref="ContractViolationException"/> and
/// <see cref="BudgetExceededException"/> anywhere in the failure chain, via
/// <see cref="TaskFailureDetails.IsCausedBy{T}"/> — the classifications the outer loop
/// can enforce without a live exception instance (doc 10 §3, hard invariant 7).
/// </summary>
internal sealed class ResiliencePolicyProvider(IFailureClassifier classifier) : IResiliencePolicyProvider
{
    /// <summary>Total pipeline timeout as a multiple of the per-attempt timeout (doc 10 §7: "total pipeline timeout (~3x attempt)").</summary>
    internal const int TotalTimeoutMultiplier = 3;

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string profileName)
    {
        var profile = ResolveProfile(profileName);

        return new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromMilliseconds(profile.TimeoutMs * TotalTimeoutMultiplier))
            .AddConcurrencyLimiter(profile.Bulkhead.MaxConcurrent, profile.Bulkhead.MaxQueue)
            .AddCircuitBreaker(BuildCircuitBreakerOptions(profile.CircuitBreaker))
            .AddRetry(BuildRetryOptions(profile.InnerRetry, classifier))
            .AddTimeout(TimeSpan.FromMilliseconds(profile.TimeoutMs))
            .Build();
    }

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
    /// True when <paramref name="details"/> or any inner failure is a <see cref="ContractViolationException"/>
    /// or <see cref="BudgetExceededException"/> — both <c>Permanent</c> in <see cref="FailureClassifier"/>.
    /// Each <see cref="TaskFailureDetails.IsCausedBy{T}"/> checks only its own level, so the chain is walked.
    /// </summary>
    internal static bool IsPermanentCause(TaskFailureDetails? details) => details switch
    {
        null => false,
        { } d when d.IsCausedBy<ContractViolationException>() || d.IsCausedBy<BudgetExceededException>() => true,
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
