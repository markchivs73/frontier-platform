using System.Text.Json;
using System.Text.Json.Nodes;

namespace Frontier.Platform.Contracts;

/// <summary>
/// Questions about the values a contract's schema describes, for checking an edge projection
/// against the contracts at either end (ADR-E27 D14). A location here is an RFC 6901 pointer into an
/// <em>instance</em> of the schema, not into the schema document. Local <c>$ref</c>s are inlined, so
/// two contracts that lay out their <c>$defs</c> differently still compare by what they accept.
/// </summary>
public static class ContractShapes
{
    private const int MaxInlineDepth = 32;

    /// <summary>
    /// The schema of the value at <paramref name="location"/> in an instance of <paramref name="root"/>,
    /// with local references inlined; <see langword="null"/> when the schema declares no such value, or
    /// describes it in a way that cannot be inlined (a recursive or non-local reference, or a reference
    /// with sibling constraints).
    /// </summary>
    public static JsonElement? ValueSchema(JsonElement root, string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        JsonElement? node = root;
        foreach (var token in Tokens(location))
        {
            node = node is { } current && Inline(root, current, 0) is { } inlined ? Step(inlined, token) : null;
        }

        return node is { } found && Inline(root, found, 0) is { } value ? ToElement(value) : null;
    }

    /// <summary>
    /// Whether every valid instance of <paramref name="root"/> has a value at <paramref name="location"/>:
    /// each step names a property its object requires. A step through an array index is never certain.
    /// </summary>
    public static bool AlwaysPresent(JsonElement root, string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        JsonElement? node = root;
        foreach (var token in Tokens(location))
        {
            if (node is not { } current || Inline(root, current, 0) is not JsonObject schema || !Requires(schema, token))
            {
                return false;
            }

            node = Step(schema, token);
        }

        return node is not null;
    }

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> accept exactly the same values: equal once annotations are removed (ADR-PA38's strict reading).</summary>
    public static bool SameShape(JsonElement left, JsonElement right) =>
        ContractCompatibility.Structure(left).AsSpan().SequenceEqual(ContractCompatibility.Structure(right));

    /// <summary>The properties an object schema requires, after inlining; empty when it is not an object schema.</summary>
    public static IReadOnlyList<string> RequiredProperties(JsonElement root) =>
        Inline(root, root, 0) is JsonObject schema && schema["required"] is JsonArray required
            ? [.. required.Select(n => n!.GetValue<string>())]
            : [];

    /// <summary>The child of an inlined schema that describes the value under <paramref name="token"/>, or null.</summary>
    internal static JsonElement? Step(JsonNode schema, string token)
    {
        var child = schema["properties"]?[token]
            ?? (IsIndex(token) ? schema["items"] as JsonObject : null)
            ?? schema["additionalProperties"] as JsonObject;
        return child is null ? null : ToElement(child);
    }

    /// <summary>
    /// <paramref name="schema"/> with every local <c>$ref</c> into <paramref name="root"/> replaced by its
    /// target, or null when a reference cannot be inlined.
    /// </summary>
    internal static JsonNode? Inline(JsonElement root, JsonElement schema, int depth)
    {
        if (depth > MaxInlineDepth)
        {
            return null;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return JsonNode.Parse(schema.GetRawText());
        }

        return schema.TryGetProperty("$ref", out var reference) ? InlineReference(root, schema, reference, depth) : InlineObject(root, schema, depth);
    }

    private static JsonNode? InlineReference(JsonElement root, JsonElement schema, JsonElement reference, int depth)
    {
        var onlyRef = schema.EnumerateObject().All(k => k.Name == "$ref" || ContractCompatibility.AnnotationKeywords.Contains(k.Name));
        var target = onlyRef && reference.GetString() is { } text && text.StartsWith('#') ? Resolve(root, text[1..]) : null;
        return target is { } found ? Inline(root, found, depth + 1) : null;
    }

    private static JsonObject? InlineObject(JsonElement root, JsonElement schema, int depth)
    {
        var inlined = new JsonObject();
        foreach (var keyword in schema.EnumerateObject().Where(k => k.Name != "$defs"))
        {
            if (InlineKeyword(root, keyword, depth) is not { } value)
            {
                return null;
            }

            inlined[keyword.Name] = value;
        }

        return inlined;
    }

    private static JsonNode? InlineKeyword(JsonElement root, JsonProperty keyword, int depth)
    {
        if (SchemaWalk.SingleKeywords.Contains(keyword.Name))
        {
            return Inline(root, keyword.Value, depth);
        }

        if (SchemaWalk.MapKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Object)
        {
            return InlineEach(root, keyword.Value.EnumerateObject().Select(p => (p.Name, p.Value)), depth);
        }

        return SchemaWalk.ArrayKeywords.Contains(keyword.Name) && keyword.Value.ValueKind == JsonValueKind.Array
            ? InlineAll(root, keyword.Value.EnumerateArray(), depth)
            : JsonNode.Parse(keyword.Value.GetRawText());
    }

    private static JsonObject? InlineEach(JsonElement root, IEnumerable<(string Name, JsonElement Value)> entries, int depth)
    {
        var map = new JsonObject();
        foreach (var (name, value) in entries)
        {
            if (Inline(root, value, depth) is not { } inlined)
            {
                return null;
            }

            map[name] = inlined;
        }

        return map;
    }

    private static JsonArray? InlineAll(JsonElement root, IEnumerable<JsonElement> items, int depth)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            if (Inline(root, item, depth) is not { } inlined)
            {
                return null;
            }

            array.Add(inlined);
        }

        return array;
    }

    /// <summary>The element at a JSON Pointer into the schema document itself, or null.</summary>
    internal static JsonElement? Resolve(JsonElement root, string pointer)
    {
        var node = root;
        foreach (var token in Tokens(pointer))
        {
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(token, out node))
            {
                return null;
            }
        }

        return node;
    }

    private static bool Requires(JsonObject schema, string token) =>
        !IsIndex(token) && schema["required"] is JsonArray required && required.Any(n => n!.GetValue<string>() == token);

    private static bool IsIndex(string token) =>
        token.Length > 0 && token.All(char.IsAsciiDigit);

    private static IEnumerable<string> Tokens(string pointer) =>
        pointer.Split('/').Skip(1).Select(t => t.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
