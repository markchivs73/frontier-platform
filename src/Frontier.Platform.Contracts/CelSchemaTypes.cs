using System.Text.Json;

using Celly.Providers;
using Celly.Types;
using Celly.Values;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The CEL types a contract's schema declares (ADR-PA38; Mark, 2026-10-29: <c>self</c> is typed from
/// the schema). An object with <c>properties</c> is a struct whose fields are its properties, so the
/// checker refuses a rule naming a field the schema does not have, and the cost estimate can ask for
/// the size of each collection a rule iterates. Every JSON number is a <c>double</c>, as the CEL spec
/// maps JSON. Anything the mapping cannot type exactly (unions, <c>oneOf</c>, a type list) is
/// <c>dyn</c>. Local <c>$ref</c>s are followed; registration has already refused any other kind.
/// </summary>
internal sealed class CelSchemaTypes : ITypeProvider, ITypeAdapter
{
    /// <summary>The prefix of every struct name: <c>contract.S0</c>, <c>contract.S1</c>…</summary>
    internal const string StructPrefix = "contract.S";

    private const int MaxRefHops = 32;

    private readonly JsonElement _root;
    private readonly Dictionary<string, CelType> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _structPointers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inProgress = new(StringComparer.Ordinal);

    /// <summary>Types the schema rooted at <paramref name="root"/>.</summary>
    internal CelSchemaTypes(JsonElement root) => _root = root;

    /// <summary>The type of <c>self</c>.</summary>
    internal CelType SelfType => TypeAt(string.Empty);

    /// <summary>The CEL type of the subschema at <paramref name="pointer"/>.</summary>
    internal CelType TypeAt(string pointer)
    {
        var canonical = Canonical(pointer);
        if (_types.TryGetValue(canonical, out var known))
        {
            return known;
        }

        if (!_inProgress.Add(canonical))
        {
            return CelType.Dyn;
        }

        var type = Derive(canonical);
        _inProgress.Remove(canonical);
        _types[canonical] = type;
        return type;
    }

    /// <summary>The subschema a field of the value at <paramref name="pointer"/> is described by: a declared property, or a map's values.</summary>
    internal string? FieldPointer(string pointer, string field)
    {
        var canonical = Canonical(pointer);
        var type = TypeAt(canonical);
        if (type.Kind == CelTypeKind.Struct)
        {
            var property = canonical + "/properties/" + SchemaWalk.Escape(field);
            return Resolve(property) is not null ? property : null;
        }

        return type.Kind == CelTypeKind.Map && Resolve(canonical + "/additionalProperties") is { ValueKind: JsonValueKind.Object }
            ? canonical + "/additionalProperties"
            : null;
    }

    /// <summary>The subschema the elements of the collection at <paramref name="pointer"/> are described by: an array's items, or a map's keys.</summary>
    internal string? ElementPointer(string pointer)
    {
        var canonical = Canonical(pointer);
        var element = TypeAt(canonical).Kind switch
        {
            CelTypeKind.List => canonical + "/items",
            CelTypeKind.Map => canonical + "/propertyNames",
            _ => null,
        };
        return element is not null && Resolve(element) is { ValueKind: JsonValueKind.Object } ? element : null;
    }

    /// <summary>The largest size the schema allows the value at <paramref name="pointer"/>: <c>maxItems</c>, <c>maxLength</c> or <c>maxProperties</c>.</summary>
    internal ulong? MaxSize(string pointer)
    {
        var canonical = Canonical(pointer);
        var keyword = TypeAt(canonical).Kind switch
        {
            CelTypeKind.List => "maxItems",
            CelTypeKind.String => "maxLength",
            CelTypeKind.Map or CelTypeKind.Struct => "maxProperties",
            _ => null,
        };
        return keyword is not null
            && Resolve(canonical) is { } node
            && node.TryGetProperty(keyword, out var limit)
            && limit.TryGetUInt64(out var max)
            ? max
            : null;
    }

    /// <summary>The pointer of a struct type, or null for a name this schema did not declare.</summary>
    internal string? StructPointer(string structName) => _structPointers.GetValueOrDefault(structName);

    /// <inheritdoc />
    public CelType? FindStructType(string name) => _structPointers.ContainsKey(name) ? CelType.Struct(name) : null;

    /// <inheritdoc />
    public CelType? FindStructFieldType(string messageName, string fieldName) =>
        StructPointer(messageName) is { } pointer && FieldPointer(pointer, fieldName) is { } field ? TypeAt(field) : null;

    /// <inheritdoc />
    public CelValue? FindIdent(string name) => null;

    /// <inheritdoc />
    public CelValue NewValue(string messageName, IReadOnlyList<KeyValuePair<string, CelValue>> fields) =>
        new ErrorValue("contract types cannot be constructed in a rule");

    /// <inheritdoc />
    public CelValue NativeToValue(object? value) => value as CelValue ?? new ErrorValue("unsupported value");

    /// <summary>The pointer <paramref name="pointer"/> ends at once local <c>$ref</c>s are followed.</summary>
    internal string Canonical(string pointer)
    {
        var current = pointer;
        for (var hop = 0; hop < MaxRefHops && LocalTarget(current) is { } target; hop++)
        {
            current = target;
        }

        return current;
    }

    /// <summary>
    /// Where the <c>$ref</c> at <paramref name="pointer"/> points, as a pointer, or null when there is
    /// no <c>$ref</c> or it names an anchor (<c>#node</c>) rather than a JSON pointer. An anchor is
    /// left untyped: the value it describes is <c>dyn</c>.
    /// </summary>
    internal string? LocalTarget(string pointer) =>
        Resolve(pointer) is { ValueKind: JsonValueKind.Object } node
        && node.TryGetProperty("$ref", out var target)
        && target.ValueKind == JsonValueKind.String
        && target.GetString() is { } reference
        && (reference == "#" || reference.StartsWith("#/", StringComparison.Ordinal))
            ? reference[1..]
            : null;

    /// <summary>The subschema at an RFC 6901 pointer, or null when there is none.</summary>
    internal JsonElement? Resolve(string pointer)
    {
        var node = _root;
        foreach (var token in pointer.Split('/').Skip(1).Select(Unescape))
        {
            if (!TryStep(node, token, out node))
            {
                return null;
            }
        }

        return node;
    }

    private CelType Derive(string pointer)
    {
        if (Resolve(pointer) is not { ValueKind: JsonValueKind.Object } node)
        {
            return CelType.Dyn;
        }

        var type = node.TryGetProperty("type", out var declared) && declared.ValueKind == JsonValueKind.String ? declared.GetString() : null;
        return type switch
        {
            "string" => CelType.String,
            "number" or "integer" => CelType.Double,
            "boolean" => CelType.Bool,
            "array" => CelType.List(Resolve(pointer + "/items") is not null ? TypeAt(pointer + "/items") : CelType.Dyn),
            "object" or null when node.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object => DeclareStruct(pointer),
            "object" => CelType.Map(CelType.String, node.TryGetProperty("additionalProperties", out var values) && values.ValueKind == JsonValueKind.Object ? TypeAt(pointer + "/additionalProperties") : CelType.Dyn),
            _ => CelType.Dyn,
        };
    }

    private CelType DeclareStruct(string pointer)
    {
        var name = StructPrefix + _structPointers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _structPointers[name] = pointer;
        return CelType.Struct(name);
    }

    private static bool TryStep(JsonElement node, string token, out JsonElement next)
    {
        next = default;
        if (node.ValueKind == JsonValueKind.Object)
        {
            return node.TryGetProperty(token, out next);
        }

        var isIndex = node.ValueKind == JsonValueKind.Array
            && int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
            && index < node.GetArrayLength();
        next = isIndex ? node[int.Parse(token, System.Globalization.CultureInfo.InvariantCulture)] : default;
        return isIndex;
    }

    private static string Unescape(string token) => token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}
