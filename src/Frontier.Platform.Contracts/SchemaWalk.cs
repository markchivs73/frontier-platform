using System.Text.Json;

namespace Frontier.Platform.Contracts;

/// <summary>Where a subschema sits relative to the schema that holds it.</summary>
internal enum SubschemaPosition
{
    /// <summary>The root schema.</summary>
    Root,

    /// <summary>The value of one <c>properties</c> entry.</summary>
    Property,

    /// <summary>A branch of <c>allOf</c>, <c>anyOf</c>, <c>oneOf</c>, <c>if</c>, <c>then</c> or <c>else</c>: the composing schema states its content model.</summary>
    Composition,

    /// <summary>Any other applicator or <c>$defs</c> entry.</summary>
    Other,
}

/// <summary>One subschema and the RFC 6901 pointer to it from the root.</summary>
internal readonly record struct Subschema(string Pointer, JsonElement Schema, SubschemaPosition Position);

/// <summary>
/// Enumerates every subschema of a JSON Schema 2020-12 document, depth first and in document
/// order, by the applicator keywords that hold subschemas. Keywords that hold data
/// (<c>enum</c>, <c>const</c>, <c>examples</c>, <c>default</c>) are never descended into, so a
/// property named <c>description</c> is a property and an example is not a schema.
/// </summary>
internal static class SchemaWalk
{
    /// <summary>Keywords whose value is one subschema.</summary>
    internal static readonly IReadOnlySet<string> SingleKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "items", "additionalProperties", "unevaluatedProperties", "unevaluatedItems", "contains",
        "propertyNames", "not", "if", "then", "else", "contentSchema",
    };

    /// <summary>Keywords whose value maps names to subschemas.</summary>
    internal static readonly IReadOnlySet<string> MapKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "$defs", "definitions", "dependentSchemas",
    };

    /// <summary>Keywords whose value is an array of subschemas.</summary>
    internal static readonly IReadOnlySet<string> ArrayKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "prefixItems",
    };

    private static readonly HashSet<string> CompositionKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "if", "then", "else",
    };

    /// <summary>The root and every subschema beneath it. Boolean subschemas are included.</summary>
    internal static IEnumerable<Subschema> All(JsonElement root)
    {
        var pending = new Stack<Subschema>();
        pending.Push(new Subschema(string.Empty, root, SubschemaPosition.Root));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            foreach (var child in Children(current).Reverse())
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>The immediate subschemas of <paramref name="parent"/>, in document order.</summary>
    internal static IEnumerable<Subschema> Children(Subschema parent)
    {
        if (parent.Schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var keyword in parent.Schema.EnumerateObject())
        {
            foreach (var child in ChildrenOf(parent.Pointer, keyword))
            {
                yield return child;
            }
        }
    }

    /// <summary>Escapes one reference token for an RFC 6901 pointer.</summary>
    internal static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static List<Subschema> ChildrenOf(string pointer, JsonProperty keyword)
    {
        var at = pointer + "/" + Escape(keyword.Name);
        var position = CompositionKeywords.Contains(keyword.Name) ? SubschemaPosition.Composition : SubschemaPosition.Other;
        if (SingleKeywords.Contains(keyword.Name))
        {
            return [new Subschema(at, keyword.Value, position)];
        }

        if (MapKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Object)
        {
            var entryPosition = keyword.Name == "properties" ? SubschemaPosition.Property : SubschemaPosition.Other;
            return keyword.Value.EnumerateObject().Select(entry => new Subschema(at + "/" + Escape(entry.Name), entry.Value, entryPosition)).ToList();
        }

        if (ArrayKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Array)
        {
            return keyword.Value.EnumerateArray().Select((item, index) => new Subschema($"{at}/{index}", item, position)).ToList();
        }

        return [];
    }
}
