using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Detects types shaped like a smart enum (TD-11) — a public instance
/// <c>string Name</c> property plus a public static <c>FromName(string)</c>
/// resolver — and creates a <see cref="SmartEnumJsonConverter{TEnum}"/> for them.
/// Registered once by <see cref="SerializationServiceCollectionExtensions.AddFrontierSerialization"/>
/// so every smart enum in the canonical profile round-trips as a snake_case string
/// without per-type converter registration.
/// </summary>
public sealed class SmartEnumJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string)
            && typeToConvert.GetMethod("FromName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(string)]) is not null;
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(SmartEnumJsonConverter<>).MakeGenericType(typeToConvert))!;
}
