using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Registry for resolving <see cref="ICachingStrategy"/> implementations by provider
/// (and optionally model/version). Used by <see cref="AssembleContextActivity"/> (S3.3 ADR-CR1)
/// to apply provider-specific cache directives to a <see cref="ContextPackage"/>.
/// </summary>
public interface ICachingStrategyRegistry
{
    /// <summary>
    /// Resolves a caching strategy for a provider/model/version combination.
    /// </summary>
    /// <param name="provider">Provider name (e.g. "anthropic", "openai").</param>
    /// <param name="modelId">Concrete model ID (e.g. "gpt-4-turbo").</param>
    /// <param name="modelVersion">Concrete model version; may be null.</param>
    /// <returns>The resolved strategy, or null if no strategy registered and no fallback available.</returns>
    ICachingStrategy? Resolve(string provider, string modelId, string? modelVersion = null);

    /// <summary>
    /// Resolves a caching strategy by provider name only.
    /// </summary>
    ICachingStrategy? ResolveStrategy(string provider);
}
