using Frontier.Platform.Abstractions;
using Microsoft.DurableTask;
using Polly;
using Polly.Retry;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>S4.4 tests for <see cref="ResiliencePolicyProvider"/> (doc 10 §2, §4, §5).</summary>
public sealed class ResiliencePolicyProviderTests
{
    private readonly ResiliencePolicyProvider provider = new(new FailureClassifier());

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
    public void HandleOuterRetryFailure_OtherException_ReturnsTrue()
    {
        var failure = TaskFailureDetails.FromException(new InvalidOperationException("transient"));

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
}
