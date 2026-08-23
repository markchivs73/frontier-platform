using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Whether a contract type can be bound as an LLM structured-output schema (ADR-DC7, S13.7h).
///
/// Providers require every object in a structured-output schema to close itself
/// (<c>additionalProperties: false</c>); Anthropic rejects the request outright otherwise
/// ("For 'object' type, 'additionalProperties' must be explicitly set to false"). An open map —
/// <c>Dictionary&lt;string, T&gt;</c> and friends — cannot satisfy that, because an open map is
/// precisely a schema whose <c>additionalProperties</c> is a *schema* rather than <c>false</c>.
///
/// The check exports the real schema through the canonical profile rather than guessing from
/// reflection, so what it accepts is what the provider will accept.
/// </summary>
internal static class StrictSchemaCheck
{
    /// <summary>Whether <paramref name="type"/> exports a schema in which every object is closed.</summary>
    internal static bool IsBindable(Type type) => FirstOpenMapPath(type) is null;

    /// <summary>
    /// The JSON path of the first open map found in <paramref name="type"/>'s exported schema, or
    /// <see langword="null"/> when the whole schema is closed. The path names the offending member
    /// so a validation finding can point the designer at it.
    /// </summary>
    internal static string? FirstOpenMapPath(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(CanonicalProfile.Options, type);
        return FindOpenMap(schema, "$");
    }

    /// <summary>Walks the schema depth-first, returning the path of the first non-false <c>additionalProperties</c>.</summary>
    private static string? FindOpenMap(JsonNode? node, string path)
    {
        if (node is JsonArray array)
        {
            return array.Select((item, i) => FindOpenMap(item, $"{path}[{i}]")).FirstOrDefault(found => found is not null);
        }

        if (node is not JsonObject obj)
        {
            return null;
        }

        if (obj.TryGetPropertyValue("additionalProperties", out var additional) && !IsFalse(additional))
        {
            return path;
        }

        return obj
            .Where(property => property.Key != "additionalProperties")
            .Select(property => FindOpenMap(property.Value, $"{path}.{property.Key}"))
            .FirstOrDefault(found => found is not null);
    }

    /// <summary>True when the node is the literal <c>false</c> — a closed object.</summary>
    private static bool IsFalse(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && !flag;
}
