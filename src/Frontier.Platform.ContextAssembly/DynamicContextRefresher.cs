using System.Diagnostics.Metrics;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Microsoft.Extensions.Logging;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Persists dynamic context refresh signals via epoch-based versioning (C-23).
/// Compares byte-level hashes; only writes on actual changes to avoid needless
/// cache invalidation (doc 04 §8, ADR-CR1).
/// </summary>
internal sealed partial class DynamicContextRefresher : IDynamicContextRefresher, IDisposable
{
    /// <summary>The meter name the doc 11 OTEL pipeline collects dynamic context metrics under.</summary>
    internal const string MeterName = "Frontier.ContextAssembly";

    private readonly IEngagementContextStore contextStore;
    private readonly Meter meter;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes the refresher with dependency injections.
    /// </summary>
    public DynamicContextRefresher(
        IEngagementContextStore contextStore,
        ILogger<DynamicContextRefresher> logger)
    {
        this.contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.meter = new(MeterName);
    }

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();

    /// <inheritdoc />
    public async Task<DynamicContextRefreshResult> RefreshDynamicAsync(
        EngagementId engagementId,
        string newDynamicContent,
        string refreshReason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engagementId);
        ArgumentNullException.ThrowIfNull(newDynamicContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshReason);

        var newHash = CanonicalProfile.Hash(newDynamicContent);
        var current = await contextStore.GetDynamicContextSnapshotAsync(engagementId, null, ct);

        // S13.62/ADR-PA24: a no-op reports the store's CURRENT epoch, never the constant 0. The
        // returned epoch becomes the run's pin, and 0 is a valid epoch — the first one — so
        // returning it from a no-op at epoch 4 silently drags the run back four epochs, after
        // which every assembly reads stale bytes while the snapshot and the signed audit record
        // attest to an epoch the run never read.
        if (current is not null && current.ContentHash == newHash)
        {
            LogNoOp(logger, engagementId, refreshReason);
            return new(Refreshed: false, Epoch: current.Epoch, ContentHash: newHash);
        }

        var newEpoch = await contextStore.UpsertDynamicContextAsync(engagementId, newDynamicContent, ct);
        LogRefreshed(logger, engagementId, newEpoch, refreshReason);
        RecordRefresh(refreshReason);

        return new(Refreshed: true, Epoch: newEpoch, ContentHash: newHash);
    }

    /// <inheritdoc />
    public async Task<DynamicContextRefreshResult> RefreshComponentsAsync(
        EngagementId engagementId,
        IReadOnlyDictionary<string, string> components,
        string refreshReason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engagementId);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshReason);

        var before = await contextStore.GetDynamicContextSnapshotAsync(engagementId, null, ct);
        var epoch = await contextStore.MergeDynamicContextAsync(engagementId, components, ct);
        var after = await contextStore.GetDynamicContextSnapshotAsync(engagementId, null, ct);

        var refreshed = before is null || before.Epoch != epoch;
        LogComponentRefresh(refreshed, engagementId, epoch, refreshReason);

        return new(refreshed, epoch, after!.ContentHash);
    }

    /// <summary>Logs and meters a component refresh outcome, so <see cref="RefreshComponentsAsync"/> stays a straight line.</summary>
    internal void LogComponentRefresh(bool refreshed, EngagementId engagementId, int epoch, string refreshReason)
    {
        if (!refreshed)
        {
            LogNoOp(logger, engagementId, refreshReason);
            return;
        }

        LogRefreshed(logger, engagementId, epoch, refreshReason);
        RecordRefresh(refreshReason);
    }

    /// <summary>Increments the doc 11 OTEL refresh counter, tagged with ADR-CR1's explicit reason.</summary>
    internal void RecordRefresh(string refreshReason) =>
        meter.CreateCounter<int>("dynamic_context_refreshed", description: "Dynamic context refreshes by reason")
            .Add(1, new KeyValuePair<string, object?>("reason", refreshReason));

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic context refresh no-op for engagement {EngagementId}: hash unchanged (reason: {RefreshReason})")]
    private static partial void LogNoOp(ILogger logger, EngagementId engagementId, string refreshReason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic context refreshed for engagement {EngagementId}: hash changed, epoch {Epoch} (reason: {RefreshReason})")]
    private static partial void LogRefreshed(ILogger logger, EngagementId engagementId, int epoch, string refreshReason);
}
