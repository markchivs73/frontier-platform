using Frontier.Platform.Serialization;
namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Minimal named caching strategy double for registry resolution tests.</summary>
internal sealed class FakeCachingStrategy(string name) : ICachingStrategy
{
    public string Name { get; } = name;

    public string ProviderName => Name;

    public CachingCapabilities GetCapabilities() =>
        new(
            SupportsExplicitDirectives: false,
            SupportsImplicitPrefixCaching: false,
            MinTokensForCaching: null,
            SupportedCacheDirectives: Array.Empty<string>());

    public Task<ProviderMessageLayout> ApplyCacheHintsAsync(ContextPackage package, CachingMetadata metadata, CancellationToken ct) =>
        Task.FromResult(new ProviderMessageLayout(
            SystemMessages: Array.Empty<object>(),
            UserMessages: Array.Empty<object>(),
            CacheDirectives: Array.Empty<ProviderCacheDirective>(),
            EstimatedTokens: 0));

    public CacheHitMetrics? ExtractCacheMetrics(object? providerResponse) => null;
}
