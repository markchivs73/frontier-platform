using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// Flattens a type's wire shape under the canonical profile into a sorted path-to-token map. The
/// platform sets no DTF data converter itself: the consumer's Host and this repo's replay host
/// configure one over the canonical profile, so that is the profile history bytes are read under.
/// Paths are JSONPath-like: <c>$</c> is the root, <c>.name</c> a member by wire name, <c>[]</c> an
/// array element, <c>{}</c> a map value, and <c>&lt;discriminator&gt;</c> one variant of a
/// polymorphic type. A required member's token ends in <see cref="RequiredSuffix"/>. An enum or
/// smart enum's token lists its wire values (<c>enum(...)</c>) and a decimal's names its scale, so
/// losing a value or changing a scale reshapes the member. CLR type names never appear in a token
/// for an object, array, map, enum or known scalar; only the <c>scalar:</c> fallback for a type the
/// reader does not know carries one, so renaming such a type reads as a reshape.
/// </summary>
internal static class WireShapeReader
{
    /// <summary>Marks a member the canonical profile requires on read.</summary>
    public const string RequiredSuffix = " (required)";

    private static readonly SmartEnumJsonConverterFactory SmartEnums = new();

    private static readonly Dictionary<Type, string> ScalarTokens = new()
    {
        [typeof(string)] = "string",
        [typeof(Guid)] = "string",
        [typeof(bool)] = "boolean",
        [typeof(int)] = "integer",
        [typeof(long)] = "integer",
        [typeof(short)] = "integer",
        [typeof(byte)] = "integer",
        [typeof(double)] = "number",
        [typeof(float)] = "number",
        [typeof(DateTime)] = "date_time",
        [typeof(DateTimeOffset)] = "date_time",
        [typeof(TimeSpan)] = "duration",
        [typeof(object)] = "any",
        [typeof(JsonElement)] = "any",
        [typeof(JsonNode)] = "any",
        [typeof(JsonObject)] = "any",
    };

    /// <summary>Reads <paramref name="type"/>'s wire shape.</summary>
    public static SortedDictionary<string, string> Read(Type type)
    {
        var members = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Visit(new Visit(type, "$", Required: false, Converter: null), members, []);
        return members;
    }

    /// <summary>Joins a member's token and its required flag into the snapshot's single string.</summary>
    public static string Mark(string token, bool required) => required ? token + RequiredSuffix : token;

    /// <summary>Whether a snapshot token marks a required member.</summary>
    public static bool IsRequired(string token) => token.EndsWith(RequiredSuffix, StringComparison.Ordinal);

    /// <summary>A snapshot token without its required marker.</summary>
    public static string Bare(string token) => IsRequired(token) ? token[..^RequiredSuffix.Length] : token;

    /// <summary>The prefix of an enum or smart enum's token, which lists its wire values.</summary>
    public const string EnumPrefix = "enum(";

    /// <summary>The token for a type whose shape <paramref name="converter"/>, the profile's converter for it, decides (<see cref="JsonTypeInfoKind.None"/>).</summary>
    public static string ScalarToken(Type type, JsonConverter converter)
    {
        if (converter is FixedPrecisionDecimalConverter fixedScale)
        {
            return $"decimal(scale={fixedScale.Scale})";
        }

        if (ScalarTokens.TryGetValue(type, out var token))
        {
            return token;
        }

        return EnumValues(type) is { } values ? EnumPrefix + string.Join('|', values) + ")"
            : SmartEnums.CanConvert(type) || type == typeof(Abstractions.EngagementId) ? "string" : "scalar:" + type.Name;
    }

    /// <summary>
    /// The wire values of an enum or of a smart enum that exposes its static <c>List</c>, in ordinal
    /// order (declaration order is not wire shape), else <see langword="null"/>. A string value is
    /// listed bare, any other as its JSON text, so a numeric enum reads <c>enum(0|1)</c>.
    /// </summary>
    public static IReadOnlyList<string>? EnumValues(Type type)
    {
        var values = type.IsEnum ? Enum.GetValues(type)
            : SmartEnums.CanConvert(type) ? type.GetProperty("List", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as IEnumerable
            : null;
        return values is null ? null : [.. values.Cast<object>().Select(value => WireText(JsonSerializer.SerializeToElement(value, type, CanonicalProfile.Options))).Order(StringComparer.Ordinal)];
    }

    /// <summary>A written value as an enum token lists it: a string bare, anything else as JSON text.</summary>
    public static string WireText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    /// <summary>The wire values an <c>enum(...)</c> token lists, or <see langword="null"/> for any other token.</summary>
    public static IReadOnlyList<string>? EnumValuesOf(string token) =>
        token.StartsWith(EnumPrefix, StringComparison.Ordinal) ? token[EnumPrefix.Length..^1].Split('|') : null;

    private static void Visit(Visit visit, SortedDictionary<string, string> members, HashSet<Type> visiting)
    {
        var type = Nullable.GetUnderlyingType(visit.Type) ?? visit.Type;
        var info = CanonicalProfile.Options.GetTypeInfo(type);
        if (info.Kind != JsonTypeInfoKind.None && !visiting.Add(type))
        {
            members[visit.Path] = Mark("recursive", visit.Required);
            return;
        }

        members[visit.Path] = Mark(Token(info, visit.Converter ?? info.Converter), visit.Required);
        foreach (var child in Children(info, visit.Path))
        {
            Visit(child, members, visiting);
        }

        visiting.Remove(type);
    }

    // An ignored member stays in the metadata with neither accessor, so it is never read or written.
    private static bool OnTheWire(JsonPropertyInfo property) => property.Get is not null || property.Set is not null;

    /// <summary>
    /// The converter a <see cref="DecimalPrecisionAttribute"/> on <paramref name="property"/> declares,
    /// else <see langword="null"/>. Read from the attribute, not <see cref="JsonPropertyInfo.CustomConverter"/>:
    /// on a <c>decimal?</c> member the serializer wraps that converter and the scale is no longer visible.
    /// </summary>
    public static JsonConverter? PrecisionOf(JsonPropertyInfo property) =>
        property.AttributeProvider!.GetCustomAttributes(typeof(DecimalPrecisionAttribute), inherit: false) is [DecimalPrecisionAttribute precision]
            ? precision.CreateConverter(typeof(decimal))
            : null;

    private static string Token(JsonTypeInfo info, JsonConverter converter) => info.Kind switch
    {
        JsonTypeInfoKind.Object when info.PolymorphismOptions is { } polymorphism => $"polymorphic({polymorphism.TypeDiscriminatorPropertyName})",
        JsonTypeInfoKind.Object => "object",
        JsonTypeInfoKind.Enumerable => "array",
        JsonTypeInfoKind.Dictionary => "map",
        _ => ScalarToken(info.Type, converter),
    };

    private static IEnumerable<Visit> Children(JsonTypeInfo info, string path) => info.Kind switch
    {
        JsonTypeInfoKind.Object when info.PolymorphismOptions is { } polymorphism =>
            polymorphism.DerivedTypes.Select(derived => new Visit(derived.DerivedType, $"{path}<{derived.TypeDiscriminator}>", Required: false, Converter: null)),
        JsonTypeInfoKind.Object => info.Properties.Where(OnTheWire).Select(property => new Visit(property.PropertyType, $"{path}.{property.Name}", property.IsRequired, PrecisionOf(property))),
        JsonTypeInfoKind.Enumerable => [new Visit(info.ElementType!, path + "[]", Required: false, Converter: null)],
        JsonTypeInfoKind.Dictionary => [new Visit(info.ElementType!, path + "{}", Required: false, Converter: null)],
        _ => [],
    };
}

/// <summary>
/// One step of <see cref="WireShapeReader"/>'s walk: the type found at a path, whether the member
/// holding it is required, and the converter a <c>[DecimalPrecision]</c> on that member declares,
/// when it overrides the profile's decimal scale.
/// </summary>
internal sealed record Visit(Type Type, string Path, bool Required, JsonConverter? Converter);
