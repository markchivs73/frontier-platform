namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Reads baseline catalogue content (fleet-wide, stable, shared context).
/// Baseline content is versioned and immutable once published.
/// Single-partition queries by catalogue ID.
/// </summary>
public interface IBaselineCatalogueStore
{
    /// <summary>
    /// Retrieve baseline catalogue content by ID.
    /// </summary>
    /// <param name="catalogueId">Identifier of the baseline catalogue (e.g. "2026-q2").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Catalogue content (JSON), or null if not found.</returns>
    Task<string?> GetBaselineCatalogueAsync(string catalogueId, CancellationToken ct);
}
