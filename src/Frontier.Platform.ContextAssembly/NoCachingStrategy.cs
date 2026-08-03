using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Fallback caching strategy: passes through context without applying cache directives.
/// Used for unknown providers/models or when caching is disabled.
/// </summary>
internal sealed class NoCachingStrategy : ICachingStrategy
{
    /// <summary>Singleton instance of the no-caching strategy.</summary>
    public static readonly NoCachingStrategy Instance = new();

    /// <inheritdoc />
    public string ProviderName => "none";

    /// <inheritdoc />
    public CachingCapabilities GetCapabilities() =>
        new(
            SupportsExplicitDirectives: false,
            SupportsImplicitPrefixCaching: false,
            MinTokensForCaching: null,
            SupportedCacheDirectives: Array.Empty<string>());

    /// <inheritdoc />
    public Task<ProviderMessageLayout> ApplyCacheHintsAsync(
        ContextPackage package,
        CachingMetadata metadata,
        CancellationToken ct)
    {
        // Return placeholder: empty system/user messages, no cache directives.
        // Real implementations (Anthropic, OpenAI) will construct actual provider message types.
        var layout = new ProviderMessageLayout(
            SystemMessages: Array.Empty<object>(),
            UserMessages: Array.Empty<object>(),
            CacheDirectives: Array.Empty<ProviderCacheDirective>(),
            EstimatedTokens: 0);

        return Task.FromResult(layout);
    }

    /// <inheritdoc />
    public CacheHitMetrics? ExtractCacheMetrics(object? providerResponse) => null;
}
