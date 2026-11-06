using System.Net;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.DurableTask;
using Polly;
using Polly.Retry;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4 tests for <see cref="ResiliencePolicyProvider"/> (doc 10 §2, §4, §5).</summary>
public sealed class ResiliencePolicyProviderTests
{
    private readonly ResiliencePolicyProvider provider = new(new FailureClassifier(), new CircuitStateProvider(TimeProvider.System), TimeProvider.System);

    [Theory]
    [InlineData("llm-default")]
    [InlineData("snapshot-persistence")]
    public void ResolveProfile_KnownProfile_ReturnsCatalogueEntry(string profileName)
    {
        var profile = ResiliencePolicyProvider.ResolveProfile(profileName);

        Assert.Equal(profileName, profile.ProfileId);
    }

    [Fact]
    public void ResolveProfile_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResiliencePolicyProvider.ResolveProfile("does-not-exist"));
    }

    [Theory]
    [InlineData("llm-default")]
    [InlineData("snapshot-persistence")]
    public void GetPipeline_KnownProfile_ReturnsPipeline(string profileName)
    {
        var pipeline = provider.GetPipeline(profileName);

        Assert.NotNull(pipeline);
    }

    /// <summary>S13.217: a pipeline is built once per profile, so its breaker and bulkhead state accumulate.</summary>
    [Fact]
    public void GetPipeline_SameProfileTwice_ReturnsTheSamePipeline()
    {
        Assert.Same(provider.GetPipeline("llm-default"), provider.GetPipeline("llm-default"));
    }

    /// <summary>S13.217: one pipeline per (profile, provider, modelId) endpoint, kept for the worker's lifetime.</summary>
    [Fact]
    public void GetPipeline_Endpoint_IsCachedPerProfileProviderAndModel()
    {
        var first = provider.GetPipeline("llm-default", "anthropic", "claude-fable-5");

        Assert.Same(first, provider.GetPipeline("llm-default", "anthropic", "claude-fable-5"));
        Assert.NotSame(first, provider.GetPipeline("llm-default", "anthropic", "claude-haiku-5"));
        Assert.NotSame(first, provider.GetPipeline("llm-interactive", "anthropic", "claude-fable-5"));
        Assert.NotSame(first, provider.GetPipeline("llm-default"));
    }

    [Fact]
    public void GetPipeline_EndpointUnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetPipeline("does-not-exist", "anthropic", "claude-fable-5"));
    }

    [Fact]
    public void GetPipeline_EndpointNullProviderOrModel_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => provider.GetPipeline("llm-default", null!, "claude-fable-5"));
        Assert.Throws<ArgumentNullException>(() => provider.GetPipeline("llm-default", "anthropic", null!));
    }

    /// <summary>S13.217: the endpoint pipeline retries a transient provider failure inside the call (doc 10 §5 inner loop).</summary>
    [Fact]
    public async Task GetPipeline_Endpoint_RetriesATransientFailure()
    {
        var pipeline = provider.BuildEndpointPipeline(FastProfile("fast", maxConcurrent: 4), "anthropic", "claude-fable-5");
        var calls = 0;

        var result = await pipeline.ExecuteAsync(_ => ++calls == 1 ? throw new TimeoutException() : ValueTask.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
    }

    /// <summary>S13.217, doc 10 §6: the bulkhead is per provider, so a saturated provider rejects calls to any of its models but not to another provider's.</summary>
    [Fact]
    public async Task BuildEndpointPipeline_SharesOneBulkheadPerProvider()
    {
        var profile = FastProfile("bulkhead", maxConcurrent: 1);
        var release = new TaskCompletionSource();
        var holding = provider.BuildEndpointPipeline(profile, "anthropic", "claude-fable-5").ExecuteAsync(async _ => await release.Task, CancellationToken.None);

        await Assert.ThrowsAsync<Polly.RateLimiting.RateLimiterRejectedException>(async () =>
            await provider.BuildEndpointPipeline(profile, "anthropic", "claude-haiku-5").ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None));
        await provider.BuildEndpointPipeline(profile, "openai", "gpt-5").ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None);

        release.SetResult();
        await holding;
    }

    /// <summary>S13.217: the bulkhead is per profile too, so a provider saturated under one profile still serves calls under another.</summary>
    [Fact]
    public async Task BuildEndpointPipeline_KeepsASeparateBulkheadPerProfile()
    {
        var release = new TaskCompletionSource();
        var saturated = FastProfile("bulkhead-a", maxConcurrent: 1);
        var holding = provider.BuildEndpointPipeline(saturated, "anthropic", "claude-fable-5").ExecuteAsync(async _ => await release.Task, CancellationToken.None);

        await Assert.ThrowsAsync<Polly.RateLimiting.RateLimiterRejectedException>(async () =>
            await provider.BuildEndpointPipeline(saturated, "anthropic", "claude-haiku-5").ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None));
        await provider.BuildEndpointPipeline(FastProfile("bulkhead-b", maxConcurrent: 1), "anthropic", "claude-haiku-5").ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None);

        release.SetResult();
        await holding;
    }

    /// <summary>S13.217: an implementation that keys no endpoint serves its profile pipeline for an endpoint request.</summary>
    [Fact]
    public void GetPipeline_EndpointDefaultImplementation_FallsBackToTheProfilePipeline()
    {
        IResiliencePolicyProvider profileOnly = new ProfileOnlyPolicyProvider();

        Assert.Same(ResiliencePipeline.Empty, profileOnly.GetPipeline("llm-default", "anthropic", "claude-fable-5"));
    }

    [Fact]
    public void TotalTimeoutMs_IsThreePerAttemptTimeouts()
    {
        Assert.Equal(270_000, ResiliencePolicyProvider.TotalTimeoutMs(Phase1ResilienceProfileCatalogue.LlmDefault));
    }

    [Fact]
    public void GetPipeline_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetPipeline("does-not-exist"));
    }

    [Fact]
    public void GetTaskOptions_UnknownProfile_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetTaskOptions("does-not-exist"));
    }

    [Fact]
    public void GetTaskOptions_LlmDefault_BuildsRetryPolicyFromProfile()
    {
        var options = provider.GetTaskOptions("llm-default");

        var policy = options.Retry!.Policy!;
        Assert.Equal(2, policy.MaxNumberOfAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), policy.FirstRetryInterval);
        Assert.Equal(2.0, policy.BackoffCoefficient);
        Assert.Equal(TimeSpan.FromMilliseconds(30_000), policy.MaxRetryInterval);
    }

    [Fact]
    public void GetTaskOptions_SnapshotPersistence_BuildsRetryPolicyFromProfile()
    {
        var options = provider.GetTaskOptions("snapshot-persistence");

        var policy = options.Retry!.Policy!;
        Assert.Equal(1, policy.MaxNumberOfAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), policy.FirstRetryInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(300_000), policy.MaxRetryInterval);
    }

    [Fact]
    public void HandleOuterRetryFailure_ContractViolation_ReturnsFalse()
    {
        var failure = TaskFailureDetails.FromException(new ContractViolationException("ScopeSection", ["bad"]));

        Assert.False(ResiliencePolicyProvider.HandleOuterRetryFailure(failure));
    }

    [Fact]
    public void HandleOuterRetryFailure_BudgetExceeded_ReturnsFalse()
    {
        var failure = TaskFailureDetails.FromException(new BudgetExceededException("advisory-sow", "invocation budget exhausted"));

        Assert.False(ResiliencePolicyProvider.HandleOuterRetryFailure(failure));
    }

    [Fact]
    public void HandleOuterRetryFailure_GuardrailsSuspended_ReturnsFalse()
    {
        var failure = TaskFailureDetails.FromException(new GuardrailsSuspendedException());

        Assert.False(ResiliencePolicyProvider.HandleOuterRetryFailure(failure));
    }

    [Fact]
    public void HandleOuterRetryFailure_GuardrailsSuspendedWrapped_ReturnsFalse()
    {
        var suspended = new TaskFailureDetails(typeof(GuardrailsSuspendedException).FullName!, "suspended", null, null, null);
        var wrapped = new TaskFailureDetails("WrapperException", "outer", null, suspended, null);

        Assert.False(ResiliencePolicyProvider.HandleOuterRetryFailure(wrapped));
    }

    [Fact]
    public void HandleOuterRetryFailure_PermanentCauseWrapped_ReturnsFalse()
    {
        var budget = new TaskFailureDetails(typeof(BudgetExceededException).FullName!, "over budget", null, null, null);
        var wrapped = new TaskFailureDetails("WrapperException", "outer", null, budget, null);

        Assert.False(ResiliencePolicyProvider.HandleOuterRetryFailure(wrapped));
    }

    [Fact]
    public void HandleOuterRetryFailure_TransientCauseWrapped_ReturnsTrue()
    {
        var inner = new TaskFailureDetails(typeof(TimeoutException).FullName!, "timed out", null, null, null);
        var wrapped = new TaskFailureDetails("WrapperException", "outer", null, inner, null);

        Assert.True(ResiliencePolicyProvider.HandleOuterRetryFailure(wrapped));
    }

    [Fact]
    public void HandleOuterRetryFailure_OtherException_ReturnsTrue()
    {
        var failure = TaskFailureDetails.FromException(new InvalidOperationException("transient"));

        Assert.True(ResiliencePolicyProvider.HandleOuterRetryFailure(failure));
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public void HandleOuterRetryFailure_CosmosWriteConflict_ReturnsTrue(HttpStatusCode lostRace)
    {
        var failure = TaskFailureDetails.FromException(new CosmosException("lost race", lostRace, 0, "activity-1", 0));

        Assert.True(ResiliencePolicyProvider.HandleOuterRetryFailure(failure));
    }

    [Fact]
    public void BuildRetryOptions_MapsInnerRetrySpecToExponentialJitteredBackoff()
    {
        var spec = Phase1ResilienceProfileCatalogue.LlmDefault.InnerRetry;

        var options = ResiliencePolicyProvider.BuildRetryOptions(spec, new FailureClassifier());

        Assert.Equal(spec.MaxAttempts - 1, options.MaxRetryAttempts);
        Assert.Equal(DelayBackoffType.Exponential, options.BackoffType);
        Assert.True(options.UseJitter);
        Assert.Equal(TimeSpan.FromMilliseconds(spec.BaseDelayMs), options.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(spec.MaxDelayMs), options.MaxDelay);
    }

    [Fact]
    public async Task BuildRetryOptions_ShouldHandle_RetryableException_ReturnsTrue()
    {
        var options = ResiliencePolicyProvider.BuildRetryOptions(Phase1ResilienceProfileCatalogue.LlmDefault.InnerRetry, new FailureClassifier());
        var outcome = Outcome.FromException<object>(new TimeoutException());
        var args = new RetryPredicateArguments<object>(ResilienceContextPool.Shared.Get(), outcome, attemptNumber: 0);

        Assert.True(await options.ShouldHandle!(args));
    }

    [Fact]
    public async Task BuildRetryOptions_ShouldHandle_PermanentException_ReturnsFalse()
    {
        var options = ResiliencePolicyProvider.BuildRetryOptions(Phase1ResilienceProfileCatalogue.LlmDefault.InnerRetry, new FailureClassifier());
        var outcome = Outcome.FromException<object>(new ContractViolationException("ScopeSection", ["bad"]));
        var args = new RetryPredicateArguments<object>(ResilienceContextPool.Shared.Get(), outcome, attemptNumber: 0);

        Assert.False(await options.ShouldHandle!(args));
    }

    [Fact]
    public async Task BuildRetryOptions_ShouldHandle_SuccessfulOutcome_ReturnsFalse()
    {
        var options = ResiliencePolicyProvider.BuildRetryOptions(Phase1ResilienceProfileCatalogue.LlmDefault.InnerRetry, new FailureClassifier());
        var outcome = Outcome.FromResult<object>("ok");
        var args = new RetryPredicateArguments<object>(ResilienceContextPool.Shared.Get(), outcome, attemptNumber: 0);

        Assert.False(await options.ShouldHandle!(args));
    }

    /// <summary>A profile with no backoff delay, so retry tests run instantly.</summary>
    internal static ResilienceProfile FastProfile(string profileId, int maxConcurrent) =>
        TimeoutHierarchyCheckTests.BuildProfile(profileId, timeoutMs: 5_000, maxAttempts: 3) with
        {
            InnerRetry = new InnerRetrySpec { MaxAttempts = 3, Backoff = "decorrelated-jitter", BaseDelayMs = 0, MaxDelayMs = 0 },
            Bulkhead = new BulkheadSpec { Scope = "provider", MaxConcurrent = maxConcurrent, MaxQueue = 0 },
        };

    [Fact]
    public void BuildCircuitBreakerOptions_MapsCircuitBreakerSpec()
    {
        var spec = Phase1ResilienceProfileCatalogue.LlmDefault.CircuitBreaker;

        var options = ResiliencePolicyProvider.BuildCircuitBreakerOptions(spec);

        Assert.Equal(spec.FailureRatio, options.FailureRatio);
        Assert.Equal(spec.MinThroughput, options.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(spec.SamplingWindowSeconds), options.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(spec.BreakDurationSeconds), options.BreakDuration);
    }

    /// <summary>An <see cref="IResiliencePolicyProvider"/> that implements only the profile-keyed members.</summary>
    private sealed class ProfileOnlyPolicyProvider : IResiliencePolicyProvider
    {
        public ResiliencePipeline GetPipeline(string profileName) => ResiliencePipeline.Empty;

        public TaskOptions GetTaskOptions(string profileName) => new();
    }
}
