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
