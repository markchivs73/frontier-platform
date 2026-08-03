using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Transforms a provider-agnostic ContextPackage into provider-specific message layouts with cache directives.
/// Each provider (Anthropic, OpenAI, etc.) has a concrete strategy that knows how to apply cache hints
/// using that provider's native caching model. Implementations must be deterministic: identical ContextPackage
/// always produces identical provider message layouts (byte-stable for audit signing and cache hit validation).
/// </summary>
public interface ICachingStrategy
{
    /// <summary>
    /// Name of the provider this strategy targets (e.g. "anthropic", "openai").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Describes caching capabilities of this provider (directives, minimum token threshold, TTL model).
    /// Used for observability and strategy selection validation.
    /// </summary>
    CachingCapabilities GetCapabilities();

    /// <summary>
    /// Transform three-tier ContextPackage into provider-specific message layout with cache directives.
    /// Must be deterministic: same ContextPackage input always produces identical output bytes.
    /// </summary>
    /// <param name="package">Provider-agnostic context package (Baseline + Dynamic + RealTime tiers).</param>
    /// <param name="metadata">Provider/model/token metadata for directive application.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Provider-specific message layout with cache directives and metadata.</returns>
    Task<ProviderMessageLayout> ApplyCacheHintsAsync(
        ContextPackage package,
        CachingMetadata metadata,
        CancellationToken ct);

    /// <summary>
    /// Extract cache hit/miss metrics from a provider response (post-invocation telemetry).
    /// Returns null if the provider response doesn't contain cache usage information.
    /// </summary>
    /// <param name="providerResponse">The provider's response object (SDK-specific type).</param>
    /// <returns>Cache metrics, or null if not available.</returns>
    CacheHitMetrics? ExtractCacheMetrics(object? providerResponse);
}

/// <summary>
/// Describes caching capabilities for a provider's strategy.
/// Used for strategy validation and observability (which strategies are in use, what they support).
/// </summary>
public sealed record CachingCapabilities(
    /// <summary>Whether this provider supports explicit cache directives (e.g., cache_control).</summary>
    bool SupportsExplicitDirectives,

    /// <summary>Whether this provider uses implicit caching (e.g., prefix-matching).</summary>
    bool SupportsImplicitPrefixCaching,

    /// <summary>Minimum token count required for caching to apply, or null if no minimum.</summary>
    int? MinTokensForCaching,

    /// <summary>Names of cache directives this strategy applies (e.g., "ephemeral", "prefix-match").</summary>
    IReadOnlyList<string> SupportedCacheDirectives);

/// <summary>
/// Provider-specific message layout produced by ICachingStrategy.ApplyCacheHintsAsync().
/// Contains the messages in provider-native types, cache metadata, and estimated token counts.
/// </summary>
public sealed record ProviderMessageLayout(
    /// <summary>System messages in provider format (e.g., Anthropic.Sdk.MessageParam).</summary>
    IReadOnlyList<object> SystemMessages,

    /// <summary>User/assistant messages in provider format (e.g., OpenAI.Chat.ChatCompletionRequestMessage).</summary>
    IReadOnlyList<object> UserMessages,

    /// <summary>Cache directives applied by this strategy.</summary>
    IReadOnlyList<ProviderCacheDirective> CacheDirectives,

    /// <summary>Estimated total tokens after cache directives (for budget/guard checks).</summary>
    int EstimatedTokens);
