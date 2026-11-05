using Polly;
using Polly.CircuitBreaker;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>
/// S13.217 tests for <see cref="CircuitStateProvider"/> (doc 10 §6): it reports the state of the breakers
/// the endpoint pipelines actually run through, and publishes their transitions.
/// </summary>
public sealed class CircuitStateProviderTests
{
    private readonly ManualTimeProvider clock = new();
    private readonly CircuitStateProvider circuits;
    private readonly ResiliencePolicyProvider policies;

    public CircuitStateProviderTests()
    {
        circuits = new CircuitStateProvider(clock);
        policies = new ResiliencePolicyProvider(new FailureClassifier(), circuits, clock);
    }

    [Fact]
    public void GetState_NoCallHasReachedThePair_ReturnsClosed()
    {
        Assert.Equal(CircuitState.Closed, circuits.GetState("anthropic", "claude-fable-5"));
    }

    /// <summary>Each endpoint has its own breaker: a sibling model of the same provider, already called and so tracked, stays Closed and keeps serving.</summary>
    [Fact]
    public async Task GetState_AfterEnoughFailures_ReportsOpen_AndPublishesTheTransition()
    {
        var transitions = new List<CircuitTransition>();
        using var subscription = circuits.Subscribe(transitions.Add);
        var pipeline = policies.GetPipeline("llm-default", "anthropic", "claude-fable-5");
        var sibling = policies.GetPipeline("llm-default", "anthropic", "claude-haiku-5");
        await sibling.ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None);

        await FailAsync(pipeline, times: 10);

        Assert.Equal(CircuitState.Open, circuits.GetState("anthropic", "claude-fable-5"));
        Assert.Equal(CircuitState.Closed, circuits.GetState("anthropic", "claude-haiku-5"));
        await sibling.ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None);
        var opened = Assert.Single(transitions);
        Assert.Equal(new CircuitTransition { Provider = "anthropic", ModelId = "claude-fable-5", From = CircuitState.Closed, To = CircuitState.Open, OccurredAtUtc = clock.GetUtcNow().UtcDateTime }, opened);
        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await pipeline.ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task GetState_AfterTheBreakDurationAndASuccessfulProbe_ReportsClosed_ThroughHalfOpen()
    {
        var transitions = new List<CircuitTransition>();
        using var subscription = circuits.Subscribe(transitions.Add);
        var pipeline = policies.GetPipeline("llm-default", "anthropic", "claude-fable-5");
        await FailAsync(pipeline, times: 10);

        clock.Advance(TimeSpan.FromSeconds(61));
        await pipeline.ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None);

        Assert.Equal(CircuitState.Closed, circuits.GetState("anthropic", "claude-fable-5"));
        Assert.Equal(
            [(CircuitState.Closed, CircuitState.Open), (CircuitState.Open, CircuitState.HalfOpen), (CircuitState.HalfOpen, CircuitState.Closed)],
            transitions.Select(t => (t.From, t.To)));
    }

    [Fact]
    public async Task GetState_APairUnderTwoProfiles_ReportsTheMostSevereBreaker()
    {
        var transitions = new List<CircuitTransition>();
        using var subscription = circuits.Subscribe(transitions.Add);
        var manual = new CircuitBreakerManualControl();
        var options = ResiliencePolicyProvider.BuildCircuitBreakerOptions(Phase1ResilienceProfileCatalogue.LlmDefault.CircuitBreaker);
        options.ManualControl = manual;
        _ = new ResiliencePipelineBuilder().AddCircuitBreaker(circuits.Track("anthropic", "claude-fable-5", options)).Build();
        _ = policies.GetPipeline("llm-interactive", "anthropic", "claude-fable-5");

        await manual.IsolateAsync();

        Assert.Equal(CircuitState.Isolated, circuits.GetState("anthropic", "claude-fable-5"));
        Assert.Equal([(CircuitState.Closed, CircuitState.Isolated)], transitions.Select(t => (t.From, t.To)));
    }

    [Fact]
    public async Task Subscribe_Disposed_StopsReceivingTransitions()
    {
        var transitions = new List<CircuitTransition>();
        var subscription = circuits.Subscribe(transitions.Add);
        subscription.Dispose();

        await FailAsync(policies.GetPipeline("llm-default", "anthropic", "claude-fable-5"), times: 10);

        Assert.Empty(transitions);
    }

    /// <summary>S13.231: no call has reached the pair, so it has no breaker and admits.</summary>
    [Fact]
    public void AdmitsCalls_NoCallHasReachedThePair_Admits()
    {
        Assert.True(circuits.AdmitsCalls("anthropic", "claude-fable-5"));
    }

    /// <summary>
    /// S13.231 (doc 10 §6): an opened breaker refuses calls during its break, then admits the probe once the break has
    /// elapsed although Polly still reports Open, refuses everything while the probe runs, and admits again once closed.
    /// </summary>
    [Fact]
    public async Task AdmitsCalls_FollowsTheBreakThenTheProbe_WhileGetStateStaysPollysTruth()
    {
        var pipeline = policies.GetPipeline("llm-default", "anthropic", "claude-fable-5");
        await FailAsync(pipeline, times: 10);
        Assert.False(circuits.AdmitsCalls("anthropic", "claude-fable-5"));

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(circuits.AdmitsCalls("anthropic", "claude-fable-5"));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(CircuitState.Open, circuits.GetState("anthropic", "claude-fable-5"));
        Assert.True(circuits.AdmitsCalls("anthropic", "claude-fable-5"));

        var release = new TaskCompletionSource();
        var probe = pipeline.ExecuteAsync(async _ => await release.Task, CancellationToken.None).AsTask();
        Assert.Equal(CircuitState.HalfOpen, circuits.GetState("anthropic", "claude-fable-5"));
        Assert.False(circuits.AdmitsCalls("anthropic", "claude-fable-5"));
        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await pipeline.ExecuteAsync(_ => ValueTask.CompletedTask, CancellationToken.None));

        release.SetResult();
        await probe;
        Assert.True(circuits.AdmitsCalls("anthropic", "claude-fable-5"));
    }

    /// <summary>S13.231: an isolated breaker never admits, however long ago it was isolated, and one refusing breaker refuses the pair.</summary>
    [Fact]
    public async Task AdmitsCalls_AnIsolatedBreakerUnderAnyProfile_RefusesThePair()
    {
        var manual = new CircuitBreakerManualControl();
        var options = ResiliencePolicyProvider.BuildCircuitBreakerOptions(Phase1ResilienceProfileCatalogue.LlmDefault.CircuitBreaker);
        options.ManualControl = manual;
        _ = new ResiliencePipelineBuilder { TimeProvider = clock }.AddCircuitBreaker(circuits.Track("anthropic", "claude-fable-5", options)).Build();
        _ = policies.GetPipeline("llm-interactive", "anthropic", "claude-fable-5");

        await manual.IsolateAsync();
        clock.Advance(TimeSpan.FromDays(1));

        Assert.False(circuits.AdmitsCalls("anthropic", "claude-fable-5"));
    }

    /// <summary>
    /// S13.231: Polly reports Open before its <c>OnOpened</c> callback has run. Until the open is published, the recorded
    /// break end is stale (a first open has none; a re-open after a failed probe has the last break's), so it refuses.
    /// </summary>
    [Theory]
    [InlineData(CircuitState.Closed, CircuitState.Closed, 0, true)]
    [InlineData(CircuitState.Open, CircuitState.Open, -1, false)]
    [InlineData(CircuitState.Open, CircuitState.Open, 0, true)]
    [InlineData(CircuitState.Open, CircuitState.Closed, 1, false)]
    [InlineData(CircuitState.Open, CircuitState.HalfOpen, 1, false)]
    [InlineData(CircuitState.HalfOpen, CircuitState.HalfOpen, 1, false)]
    [InlineData(CircuitState.Isolated, CircuitState.Isolated, 1, false)]
    public void CircuitAdmission_AdmitsOnlyClosed_OrAPublishedOpenWhoseBreakHasEnded(CircuitState polly, CircuitState published, int secondsPastBreakEnd, bool expected)
    {
        var breakEndsAt = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, CircuitAdmission.Admits(polly, published, breakEndsAt, breakEndsAt.AddSeconds(secondsPastBreakEnd)));
    }

    /// <summary>S13.231: an implementation that cannot see break timing admits only Closed and HalfOpen.</summary>
    [Theory]
    [InlineData(CircuitState.Closed, true)]
    [InlineData(CircuitState.HalfOpen, true)]
    [InlineData(CircuitState.Open, false)]
    [InlineData(CircuitState.Isolated, false)]
    public void AdmitsCalls_DefaultImplementation_AdmitsOnlyClosedAndHalfOpen(CircuitState state, bool expected)
    {
        ICircuitStateProvider fixedState = new FixedStateProvider(state);

        Assert.Equal(expected, fixedState.AdmitsCalls("anthropic", "claude-fable-5"));
    }

    [Fact]
    public void Subscribe_NullCallback_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => circuits.Subscribe(null!));
    }

    [Theory]
    [InlineData(CircuitState.Closed, 0)]
    [InlineData(CircuitState.HalfOpen, 1)]
    [InlineData(CircuitState.Open, 2)]
    [InlineData(CircuitState.Isolated, 3)]
    public void Severity_RanksStatesThatBlockTrafficHighest(CircuitState state, int expected)
    {
        Assert.Equal(expected, CircuitStateProvider.Severity(state));
    }

    [Fact]
    public void MostSevere_PicksTheHighestRankedState()
    {
        Assert.Equal(CircuitState.Open, CircuitStateProvider.MostSevere([CircuitState.Closed, CircuitState.Open, CircuitState.HalfOpen]));
    }

    /// <summary>An <see cref="ICircuitStateProvider"/> that reports one fixed state and keeps the default <c>AdmitsCalls</c>.</summary>
    private sealed class FixedStateProvider(CircuitState state) : ICircuitStateProvider
    {
        public CircuitState GetState(string provider, string modelId) => state;

        public IDisposable Subscribe(Action<CircuitTransition> onTransition) => throw new NotSupportedException();
    }

    /// <summary>Fails <paramref name="times"/> calls with an unclassified (permanent, never-retried) exception, so each is one breaker sample.</summary>
    private static async Task FailAsync(ResiliencePipeline pipeline, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pipeline.ExecuteAsync(_ => throw new InvalidOperationException("provider down"), CancellationToken.None));
        }
    }
}
