
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Validates context assembly requests: checks that all requested baseline components
/// exist in the catalogue (doc 04 §10 scenario 2 — unknown baseline component at assembly time).
/// </summary>
internal sealed class ContextValidator : IContextValidator
{
    /// <inheritdoc />
    public IReadOnlyList<string> ValidateRequest(ContextRequest request, BaselineCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalogue);

        var errors = new List<string>();
        var catalogueComponents = new HashSet<string>(catalogue.Components.Keys, StringComparer.Ordinal);

        foreach (var component in request.BaselineComponents)
        {
            if (!catalogueComponents.Contains(component))
            {
                errors.Add($"Unknown baseline component '{component}' (not found in catalogue '{catalogue.CatalogueId}')");
            }
        }

        return errors.AsReadOnly();
    }
}
