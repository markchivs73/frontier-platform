using Microsoft.Azure.Cosmos;
using Xunit;

namespace Frontier.Platform.Audit.Tests;

public sealed class ChangeFeedProcessorLifecycleTests
{
    [Fact]
    public async Task StopBeforeStartIsANoOp()
    {
        var lifecycle = new ChangeFeedProcessorLifecycle(_ => throw new InvalidOperationException("never called"), TimeSpan.Zero);

        await lifecycle.StopAsync();
    }

    [Fact]
    public async Task StartThenStopStopsTheStartedProcessorOnce()
    {
        var processor = new FakeProcessor();
        var lifecycle = new ChangeFeedProcessorLifecycle(async _ => { await processor.StartAsync(); return processor; }, TimeSpan.Zero);

        await lifecycle.StartWithRetryAsync(CancellationToken.None);
        await lifecycle.StopAsync();
        await lifecycle.StopAsync();

        Assert.Equal(1, processor.Starts);
        Assert.Equal(1, processor.Stops);
    }

    [Fact]
    public async Task AFailedStartIsNotKeptAndTheRetryKeepsOnlyTheStartedProcessor()
    {
        var failed = new FakeProcessor { FailStart = true };
        var started = new FakeProcessor();
        var attempts = 0;
        var lifecycle = new ChangeFeedProcessorLifecycle(async _ =>
        {
            var processor = attempts++ == 0 ? failed : started;
            await processor.StartAsync();
            return processor;
        }, TimeSpan.Zero);

        await lifecycle.StartWithRetryAsync(CancellationToken.None);
        await lifecycle.StopAsync();

        Assert.Equal(0, failed.Stops);
        Assert.Equal(1, started.Stops);
    }

    [Fact]
    public async Task AStartThatFailsUntilCancelledLeavesTheServiceStoppable()
    {
        using var cancellation = new CancellationTokenSource();
        var failed = new FakeProcessor { FailStart = true };
        var lifecycle = new ChangeFeedProcessorLifecycle(async _ =>
        {
            await cancellation.CancelAsync();
            await failed.StartAsync();
            return failed;
        }, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.StartWithRetryAsync(cancellation.Token));
        await lifecycle.StopAsync();

        Assert.Equal(0, failed.Stops);
    }

    [Fact]
    public async Task StartWithAnAlreadyCancelledTokenStartsNothing()
    {
        var lifecycle = new ChangeFeedProcessorLifecycle(_ => throw new InvalidOperationException("never called"), TimeSpan.Zero);

        await lifecycle.StartWithRetryAsync(new CancellationToken(canceled: true));
        await lifecycle.StopAsync();
    }

    [Fact]
    public async Task CancellationDuringTheRetryDelayEndsTheStart()
    {
        using var cancellation = new CancellationTokenSource();
        var lifecycle = new ChangeFeedProcessorLifecycle(_ => throw new InvalidOperationException("start failed"), TimeSpan.FromMinutes(5));

        var start = lifecycle.StartWithRetryAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await lifecycle.StopAsync();
    }

    private sealed class FakeProcessor : ChangeFeedProcessor
    {
        public bool FailStart { get; init; }

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public override Task StartAsync()
        {
            if (FailStart)
            {
                throw new InvalidOperationException("start failed");
            }

            Starts++;
            return Task.CompletedTask;
        }

        public override Task StopAsync()
        {
            Stops++;
            return FailStart ? throw new InvalidOperationException("half-built processor") : Task.CompletedTask;
        }
    }
}
