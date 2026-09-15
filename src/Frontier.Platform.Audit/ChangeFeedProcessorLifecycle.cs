using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Audit;

/// <summary>
/// The safe start/stop lifecycle shared by the archival hosted services (doc 05 §8, ADR-PA30): a processor is
/// kept only once its start succeeds, so a failed start never leaves a half-built processor for shutdown to
/// stop, and stopping is null-safe and idempotent.
/// </summary>
/// <param name="startProcessorAsync">Builds and starts a processor; throws when the start fails.</param>
/// <param name="retryDelay">The wait between failed start attempts.</param>
internal sealed class ChangeFeedProcessorLifecycle(
    Func<CancellationToken, Task<ChangeFeedProcessor>> startProcessorAsync,
    TimeSpan retryDelay)
{
    private ChangeFeedProcessor? processor;

    /// <summary>Starts the processor, retrying failed starts until one succeeds or <paramref name="cancellationToken"/> is cancelled.</summary>
    internal async Task StartWithRetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                processor = await startProcessorAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
#pragma warning disable CA1031 // Retry loop must catch any transient startup failure (SSL, HTTP, Cosmos) without knowing all concrete types the SDK may throw
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Stops the started processor, if any; a second call, or a call with nothing started, does nothing.</summary>
    internal async Task StopAsync()
    {
        var started = Interlocked.Exchange(ref processor, null);
        if (started is not null)
        {
            await started.StopAsync().ConfigureAwait(false);
        }
    }
}
