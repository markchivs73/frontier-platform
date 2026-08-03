namespace Frontier.Platform.Observability.Tests;

/// <summary>S6.8 tests for <see cref="Phase1ExecutionMonitorFeed"/> (doc 11 §9).</summary>
public sealed class Phase1ExecutionMonitorFeedTests
{
    private readonly Phase1ExecutionMonitorFeed _feed = new();

    [Fact]
    public async Task SubscribeAsync_ReturnsEmptyStream()
    {
        var events = new List<ExecutionMetricEvent>();

        await foreach (var evt in _feed.SubscribeAsync("exec-1", CancellationToken.None))
            events.Add(evt);

        Assert.Empty(events);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleCallsReturnEmptyStreams()
    {
        var first = _feed.SubscribeAsync("exec-1", CancellationToken.None);
        var second = _feed.SubscribeAsync("exec-2", CancellationToken.None);

        var events1 = new List<ExecutionMetricEvent>();
        var events2 = new List<ExecutionMetricEvent>();

        await foreach (var e in first) events1.Add(e);
        await foreach (var e in second) events2.Add(e);

        Assert.Empty(events1);
        Assert.Empty(events2);
    }

    [Fact]
    public void EmptyAsyncEnumerable_GetAsyncEnumerator_ReturnsSingleton()
    {
        var e1 = Phase1ExecutionMonitorFeed.EmptyAsyncEnumerable.Instance.GetAsyncEnumerator();
        var e2 = Phase1ExecutionMonitorFeed.EmptyAsyncEnumerable.Instance.GetAsyncEnumerator();

        Assert.Same(e1, e2);
    }

    [Fact]
    public async Task EmptyAsyncEnumerator_MoveNextAsync_ReturnsFalse()
    {
        var result = await Phase1ExecutionMonitorFeed.EmptyAsyncEnumerator.Instance.MoveNextAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task EmptyAsyncEnumerator_DisposeAsync_Completes()
    {
        await Phase1ExecutionMonitorFeed.EmptyAsyncEnumerator.Instance.DisposeAsync();
        // no exception = pass
    }
}
