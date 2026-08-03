namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Version-aware registry for resolving ICachingStrategy implementations by (provider, model, version).
/// Supports fallback chains: exact match → model any-version → provider any-model → global fallback.
/// </summary>
public sealed class CachingStrategyRegistry : ICachingStrategyRegistry
{
    private readonly List<StrategyRegistration> _registrations;
    private readonly ICachingStrategy _fallbackStrategy;

    public CachingStrategyRegistry(ICachingStrategy fallbackStrategy)
    {
        _registrations = new();
        _fallbackStrategy = fallbackStrategy ?? throw new ArgumentNullException(nameof(fallbackStrategy));
    }

    /// <summary>
    /// Register a caching strategy for a provider/model/version combination.
    /// Patterns support wildcards: "*" matches any value.
    /// </summary>
    /// <param name="provider">Provider name (e.g. "anthropic", "openai"). Use "*" to match any.</param>
    /// <param name="modelPattern">Model ID pattern (e.g. "gpt-4-*", "claude-*"). Use "*" to match any.</param>
    /// <param name="versionPattern">Model version pattern (e.g. "2024-*", "1.*"). Use null to match any version.</param>
    /// <param name="strategy">The caching strategy implementation.</param>
    public void Register(string provider, string modelPattern, string? versionPattern, ICachingStrategy strategy)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty.", nameof(provider));
        if (string.IsNullOrWhiteSpace(modelPattern))
            throw new ArgumentException("Model pattern cannot be empty.", nameof(modelPattern));

        _registrations.Add(new(
            Provider: provider,
            ModelPattern: modelPattern,
            VersionPattern: versionPattern,
            Strategy: strategy ?? throw new ArgumentNullException(nameof(strategy))));
    }

    /// <summary>
    /// Resolve a caching strategy for a provider/model/version combination.
    /// Uses fallback chain: exact match → model any-version → provider any-model → global fallback.
    /// </summary>
    /// <param name="provider">Provider name (e.g. "anthropic", "openai").</param>
    /// <param name="modelId">Concrete model ID (e.g. "gpt-4-turbo").</param>
    /// <param name="modelVersion">Concrete model version (e.g. "2024-12"), or null if version unknown.</param>
    /// <returns>The resolved strategy, or the fallback if no match found.</returns>
    public ICachingStrategy Resolve(string provider, string modelId, string? modelVersion = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider cannot be empty.", nameof(provider));
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Model ID cannot be empty.", nameof(modelId));

        // 1. Try exact match: provider + model pattern + version pattern
        if (modelVersion != null)
        {
            var exact = _registrations.FirstOrDefault(r =>
                Matches(r.Provider, provider) &&
                Matches(r.ModelPattern, modelId) &&
                r.VersionPattern != null && Matches(r.VersionPattern, modelVersion));
            if (exact != null)
                return exact.Strategy;
        }

        // 2. Try model with any-version: provider + model pattern (no version)
        var modelAny = _registrations.FirstOrDefault(r =>
            Matches(r.Provider, provider) &&
            Matches(r.ModelPattern, modelId) &&
            r.VersionPattern == null);
        if (modelAny != null)
            return modelAny.Strategy;

        // 3. Try provider any-model: provider + "*" pattern
        var providerDefault = _registrations.FirstOrDefault(r =>
            Matches(r.Provider, provider) &&
            Matches(r.ModelPattern, "*"));
        if (providerDefault != null)
            return providerDefault.Strategy;

        // 4. Fall back to global fallback
        return _fallbackStrategy;
    }

    /// <summary>
    /// Resolve a caching strategy by provider name only (all models/versions use provider default).
    /// Returns the global fallback if no provider-specific strategy registered.
    /// </summary>
    /// <param name="provider">Provider name (e.g. "anthropic", "openai").</param>
    /// <returns>The resolved strategy, or null if none found.</returns>
    public ICachingStrategy? ResolveStrategy(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        var providerDefault = _registrations.FirstOrDefault(r =>
            Matches(r.Provider, provider) &&
            Matches(r.ModelPattern, "*"));
        return providerDefault?.Strategy ?? _fallbackStrategy;
    }

    /// <summary>
    /// Check if a concrete value matches a pattern (supports "*" wildcard).
    /// </summary>
    private static bool Matches(string pattern, string value)
    {
        if (pattern == "*")
            return true;

        // Simple wildcard matching: "gpt-4-*" matches "gpt-4-turbo", "gpt-4-vision", etc.
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }

        return pattern == value;
    }

    private sealed record StrategyRegistration(
        string Provider,
        string ModelPattern,
        string? VersionPattern,
        ICachingStrategy Strategy);
}
