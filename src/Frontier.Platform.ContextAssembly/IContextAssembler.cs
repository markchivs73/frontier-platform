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

    /// <summary>
    /// Assembles as <see cref="AssembleAsync"/>
    /// does, and stamps the package's <see cref="BaselineTier"/> with the catalogue release and
    /// components <paramref name="baselineContent"/> was rendered from (S13.218, doc 04 §4 step 2).
    /// </summary>
    /// <remarks>
    /// The default implementation assembles through <see cref="AssembleAsync"/> and then replaces the
    /// baseline tier's version and components, so an existing implementation stays correct
    /// without change; the platform's own assembler stamps them directly.
    /// </remarks>
    /// <param name="metadata">Provider/model/version metadata for cache-strategy resolution.</param>
    /// <param name="baselineContent">Fleet-wide stable context content (pre-composed).</param>
    /// <param name="dynamicContent">Engagement-specific content (pre-composed).</param>
    /// <param name="realTimeContent">Per-invocation signals (pre-composed).</param>
    /// <param name="baselineProvenance">The baseline catalogue release and components read.</param>
    /// <param name="dynamicProvenance">Where <paramref name="dynamicContent"/> was read from, or <see langword="null"/> when nothing was stored to cite.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Package whose baseline tier cites <paramref name="baselineProvenance"/>.</returns>
    async Task<ContextPackage> AssembleWithProvenanceAsync(
        CachingMetadata metadata,
        string baselineContent,
        string dynamicContent,
        string realTimeContent,
        BaselineTierProvenance baselineProvenance,
        DynamicTierProvenance? dynamicProvenance,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(baselineProvenance);

        var package = await AssembleAsync(metadata, baselineContent, dynamicContent, realTimeContent, dynamicProvenance, ct).ConfigureAwait(false);
        var baseline = package.Baseline with { BaselineVersion = baselineProvenance.BaselineVersion, Components = baselineProvenance.Components };

        return package with { Baseline = baseline };
    }
}
