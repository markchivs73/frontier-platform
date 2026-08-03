using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Cache read/write activity for one context tier, aggregated across an execution's
/// <see cref="AgentInvocation"/>s (doc 05 §6 <c>cacheMetrics</c> shape). Per C-15, counts
/// derive from whether the tier's content changed since its last cache breakpoint;
/// <see cref="TokensRead"/> is attributed wholly to whichever tier's breakpoint was hit
/// on a given invocation (Anthropic reports aggregate cache tokens, not per-breakpoint).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record CacheTierMetrics
{
    /// <summary>Number of invocations that hit this tier's cache breakpoint.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("reads")]
    public required int Reads { get; init; }

    /// <summary>Number of invocations that wrote (refreshed) this tier's cache breakpoint.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("writes")]
    public required int Writes { get; init; }

    /// <summary>Hit rate for this tier across the execution, as a percentage (0-100).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("hit_rate_percent")]
    public required decimal HitRatePercent { get; init; }

    /// <summary>Cache-read tokens attributed to this tier.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("tokens_read")]
    public required long TokensRead { get; init; }
}
