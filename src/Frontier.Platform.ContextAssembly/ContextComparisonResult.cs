using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Structured comparison of context packages (current vs. previous) for debugging
/// and observability (doc 04 §11, C-24). Per-tier verdicts, hashes, and diff markers.
/// The Blazor `&lt;ContextDebugger&gt;` component (collapsible panes, per-tier cache verdict,
/// diff view) is deferred to Stage 9/doc 19 — this task provides the data only.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by ContextDebuggerComparisonTests.")]
public sealed record ContextComparisonResult(
    BaselineTierComparison BaselineComparison,
    DynamicTierComparison DynamicComparison,
    RealTimeTierComparison? RealTimeComparison)
{
    /// <summary>Comparison for the baseline tier.</summary>
    public BaselineTierComparison BaselineComparison { get; } = BaselineComparison;

    /// <summary>Comparison for the dynamic tier.</summary>
    public DynamicTierComparison DynamicComparison { get; } = DynamicComparison;

    /// <summary>Comparison for the real-time tier (null if not present).</summary>
    public RealTimeTierComparison? RealTimeComparison { get; } = RealTimeComparison;
}

/// <summary>
/// Per-tier comparison result with content hash, cache verdict, and diff markers.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by ContextDebuggerComparisonTests.")]
public record TierComparisonBase(
    string ContentHash,
    string CacheVerdict,
    bool ChangedFromPrevious)
{
    /// <summary>Canonical hash of the tier's content.</summary>
    public string ContentHash { get; } = ContentHash;

    /// <summary>Cache verdict for this tier (e.g., "cached", "not_cached", "partial").</summary>
    public string CacheVerdict { get; } = CacheVerdict;

    /// <summary>Whether this tier's hash differs from the previous comparison (if supplied).</summary>
    public bool ChangedFromPrevious { get; } = ChangedFromPrevious;
}

/// <summary>
/// Baseline tier comparison.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by ContextDebuggerComparisonTests.")]
public sealed record BaselineTierComparison(
    string ContentHash,
    string CacheVerdict,
    bool ChangedFromPrevious) : TierComparisonBase(ContentHash, CacheVerdict, ChangedFromPrevious);

/// <summary>
/// Dynamic tier comparison.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by ContextDebuggerComparisonTests.")]
public sealed record DynamicTierComparison(
    string ContentHash,
    string CacheVerdict,
    bool ChangedFromPrevious,
    int? EpochIfAvailable) : TierComparisonBase(ContentHash, CacheVerdict, ChangedFromPrevious)
{
    /// <summary>The epoch of the dynamic context (if available from refresh metadata).</summary>
    public int? EpochIfAvailable { get; } = EpochIfAvailable;
}

/// <summary>
/// Real-time tier comparison.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; properties are exercised by ContextDebuggerComparisonTests.")]
public sealed record RealTimeTierComparison(
    string ContentHash,
    string CacheVerdict,
    bool ChangedFromPrevious,
    int FetchCount) : TierComparisonBase(ContentHash, CacheVerdict, ChangedFromPrevious)
{
    /// <summary>Number of real-time sources fetched for this tier.</summary>
    public int FetchCount { get; } = FetchCount;
}
