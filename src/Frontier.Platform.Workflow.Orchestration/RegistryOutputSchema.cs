using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

using Frontier.Platform.Abstractions;

using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Lowers a registry contract's schema to a structured-output format (ADR-E27 D7). The format is a
/// generation aid: whatever the model returns, the engine validates it against the original contract.
/// <para>
/// The lowering targets what every provider's strict mode accepts. Annotations and constraints that
/// only narrow a value (lengths, ranges, <c>pattern</c>, <c>format</c>) are dropped, since validation
/// enforces them anyway. A schema the providers cannot express is refused (<see langword="null"/>), and
/// the call goes out with no format: an object that is open or has optional properties, a composition
/// other than <c>anyOf</c>, a conditional, or any keyword not listed here.
/// </para>
/// </summary>
internal static class RegistryOutputSchema
{
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items", "enum", "const", "anyOf", "$ref", "$defs", "description", "title",
    };

    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$comment", "examples", "default", "deprecated", "readOnly", "writeOnly", "format",
        "contentEncoding", "contentMediaType", "contentSchema", "minLength", "maxLength", "pattern",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minItems", "maxItems",
        "uniqueItems", "minProperties", "maxProperties",
    };

    private static readonly ConcurrentDictionary<string, ChatResponseFormat?> Cache = new(StringComparer.Ordinal);

    /// <summary>The format for <paramref name="contract"/>, or <see langword="null"/> when its schema cannot be lowered.</summary>
    internal static ChatResponseFormat? For(ContractSnapshot contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return Cache.GetOrAdd(contract.Hash, _ => Lower(JsonNode.Parse(contract.Schema.GetRawText())) is { } lowered
            ? ChatResponseFormat.ForJsonSchema(ToElement(lowered), SchemaName(contract.ContractRef))
            : null);
    }

    /// <summary>The lowered copy of <paramref name="node"/>, or null when any part of it cannot be lowered.</summary>
    internal static JsonObject? Lower(JsonNode? node)
    {
        if (node is not JsonObject schema || !ObjectIsClosedAndComplete(schema))
        {
            return null;
        }

        var lowered = new JsonObject();
        foreach (var (keyword, value) in schema)
        {
            if (Dropped.Contains(keyword))
            {
                continue;
            }

            if (!Kept.Contains(keyword) || LowerKeyword(keyword, value) is not { } kept)
            {
                return null;
            }

            lowered[keyword] = kept;
        }

        return lowered;
    }

    /// <summary>The lowered value of one kept keyword, or null when it cannot be lowered.</summary>
    internal static JsonNode? LowerKeyword(string keyword, JsonNode? value) => keyword switch
    {
        "properties" or "$defs" => LowerEach(value as JsonObject),
        "items" => Lower(value),
        "anyOf" => LowerAll(value as JsonArray),
        _ => value?.DeepClone(),
    };

    /// <summary>Every subschema of a name-to-schema map, lowered; null when one cannot be.</summary>
    internal static JsonObject? LowerEach(JsonObject? map)
    {
        if (map is null)
        {
            return null;
        }

        var lowered = new JsonObject();
        foreach (var (name, subschema) in map)
        {
            if (Lower(subschema) is not { } kept)
            {
                return null;
            }

            lowered[name] = kept;
        }

        return lowered;
    }

    /// <summary>Every subschema of a list, lowered; null when one cannot be.</summary>
    internal static JsonArray? LowerAll(JsonArray? list)
    {
        if (list is null)
        {
            return null;
        }

        var lowered = new JsonArray();
        foreach (var subschema in list)
        {
            if (Lower(subschema) is not { } kept)
            {
                return null;
            }

            lowered.Add(kept);
        }

        return lowered;
    }

    /// <summary>
    /// Whether an object schema (one with <c>properties</c>, or of type <c>object</c>) is closed
    /// (<c>additionalProperties: false</c>) and requires every property, as strict modes demand. Any
    /// other schema passes, unless it carries <c>additionalProperties</c> at all.
    /// </summary>
    internal static bool ObjectIsClosedAndComplete(JsonObject schema)
    {
        var properties = schema["properties"] as JsonObject;
        var isObject = properties is not null || (schema["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "object");
        if (!isObject)
        {
            return !schema.ContainsKey("additionalProperties");
        }

        var required = (schema["required"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        return IsFalse(schema["additionalProperties"]) && (properties ?? []).All(p => required.Contains(p.Key));
    }

    private static JsonElement ToElement(JsonObject schema)
    {
        using var document = JsonDocument.Parse(schema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static bool IsFalse(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && !flag;

    /// <summary>A provider-safe format name from a contract ref: letters, digits, <c>_</c> and <c>-</c> only.</summary>
    internal static string SchemaName(string contractRef) =>
        new([.. contractRef.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' ? c : '_')]);
}
