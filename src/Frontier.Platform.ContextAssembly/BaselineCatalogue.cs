namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// A baseline catalogue (doc 04 §6): a named collection of component templates
/// (e.g. firm standards, playbooks) indexed by component name. Used by validators
/// to check that requested components are known before assembly.
/// </summary>
public sealed record BaselineCatalogue(
    string CatalogueId,
    IReadOnlyDictionary<string, string> Components)
{
    /// <summary>The catalogue's versioned identifier.</summary>
    public string CatalogueId { get; } = CatalogueId;

    /// <summary>Components in this catalogue, indexed by name.</summary>
    public IReadOnlyDictionary<string, string> Components { get; } = Components;
}
