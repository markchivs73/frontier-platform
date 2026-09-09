using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Applies provider-specific caching strategy directives to a <see cref="ContextPackage"/>
/// (S3.2 design, S3.3 ADR-CR1). Does not compose tiers (that's a caller concern); focuses
/// on resolving the caching strategy and augmenting the package with cache hints.
/// </summary>
public interface IContextAssembler
{
    /// <summary>
    /// Applies provider-specific caching strategy directives to an already-assembled context package.
    /// </summary>
    /// <param name="metadata">Provider/model/version metadata for cache-strategy resolution.</param>
    /// <param name="baselineContent">Fleet-wide stable context content (pre-composed).</param>
    /// <param name="dynamicContent">Engagement-specific content (pre-composed).</param>
    /// <param name="realTimeContent">Per-invocation signals (pre-composed).</param>
    /// <param name="dynamicProvenance">
    /// Where <paramref name="dynamicContent"/> was read from (S13.60, doc 04 §4 step 3), so the
    /// package's <see cref="DynamicTier"/> cites the engagement, epoch and epoch document; or
    /// <see langword="null"/> when nothing was stored to cite.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Package with provider-specific cache directives applied.</returns>
    Task<ContextPackage> AssembleAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        DynamicTierProvenance? dynamicProvenance = null,
        CancellationToken ct = default);
}
