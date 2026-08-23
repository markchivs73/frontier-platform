using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Filters a store's whole-catalogue/whole-engagement JSON down to the named fields a
/// node's <see cref="ContextRequest"/> asked for (doc 03 §2), in the order requested.
/// Used by <see cref="ContextContentComposer"/> to turn
/// <see cref="Frontier.Platform.ContextAssembly.IBaselineCatalogueStore"/>/
/// <see cref="Frontier.Platform.ContextAssembly.IEngagementContextStore"/> JSON into the
/// per-tier content strings <see cref="Frontier.Platform.ContextAssembly.IContextAssembler"/>
/// expects.
/// </summary>
internal static class ContextContentFilter
{
    /// <summary>
    /// Returns the canonical-JSON object containing only <paramref name="keys"/> from
    /// <paramref name="sourceJson"/>, in <paramref name="keys"/> order. Returns
    /// <c>"{}"</c> if <paramref name="keys"/> is empty (the node requested nothing from
    /// this tier). Throws <see cref="ContractViolationException"/> (named
    /// <paramref name="fieldName"/>, permanent per the two-loop model) if any key is
    /// absent from <paramref name="sourceJson"/>.
    /// </summary>
    internal static string Filter(string sourceJson, IReadOnlyList<string> keys, string fieldName)
    {
        if (keys.Count == 0)
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(sourceJson);

        var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var key in keys)
        {
            if (document.RootElement.TryGetProperty(key, out var value))
            {
                filtered[key] = value.Clone();
            }
            else
            {
                missing.Add($"missing field '{key}'");
            }
        }

        if (missing.Count > 0)
        {
            throw new ContractViolationException(fieldName, missing);
        }

        return JsonSerializer.Serialize(filtered, CanonicalProfile.Options);
    }
}
