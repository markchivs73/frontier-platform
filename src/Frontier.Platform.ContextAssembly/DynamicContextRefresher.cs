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
        var currentContent = await contextStore.GetDynamicContextAsync(engagementId, ct);

        if (currentContent is not null)
        {
            var currentHash = CanonicalProfile.Hash(currentContent);
            if (currentHash == newHash)
            {
                LogNoOp(logger, engagementId, refreshReason);
                return new(Refreshed: false, Epoch: 0, ContentHash: newHash);
            }
        }

        var newEpoch = await contextStore.UpsertDynamicContextAsync(engagementId, newDynamicContent, ct);
        LogRefreshed(logger, engagementId, newEpoch, refreshReason);

        meter.CreateCounter<int>("dynamic_context_refreshed", description: "Dynamic context refreshes by reason")
            .Add(1, new KeyValuePair<string, object?>("reason", refreshReason));

        return new(Refreshed: true, Epoch: newEpoch, ContentHash: newHash);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic context refresh no-op for engagement {EngagementId}: hash unchanged (reason: {RefreshReason})")]
    private static partial void LogNoOp(ILogger logger, EngagementId engagementId, string refreshReason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic context refreshed for engagement {EngagementId}: hash changed, epoch {Epoch} (reason: {RefreshReason})")]
    private static partial void LogRefreshed(ILogger logger, EngagementId engagementId, int epoch, string refreshReason);
}
