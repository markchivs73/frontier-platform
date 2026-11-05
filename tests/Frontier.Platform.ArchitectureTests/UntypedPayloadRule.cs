using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// S13.236 (K4), the platform mirror of the consumer's rule (S13.213): finds the properties of a
/// serialized contract whose declared type carries no shape — <see cref="object"/>,
/// <see cref="JsonElement"/>, <see cref="JsonDocument"/> or a <see cref="JsonNode"/> — directly,
/// as <c>T?</c>, as an array element, or as any generic argument (a
/// <c>Dictionary&lt;string, object&gt;</c> is as untyped as an <c>object</c>).
/// </summary>
internal static class UntypedPayloadRule
{
    /// <summary>The contract's serialized untyped properties, as <c>Namespace.Type.Property</c>.</summary>
    internal static IEnumerable<string> Offenders(Type contract) =>
        SerializedContracts.DeclaredProperties(contract)
            .Where(IsSerialized)
            .Where(p => IsUntyped(p.PropertyType))
            .Select(p => $"{contract.FullName}.{p.Name}");

    /// <summary>True when <paramref name="type"/> is, or is built from, a type with no declared shape.</summary>
    internal static bool IsUntyped(Type type) =>
        type == typeof(object)
        || type == typeof(JsonElement)
        || type == typeof(JsonDocument)
        || typeof(JsonNode).IsAssignableFrom(type)
        || (type.IsArray && IsUntyped(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(IsUntyped));

    /// <summary>
    /// K4's envelope-plus-<c>schema_ref</c> (ADR-E2): true when <paramref name="contract"/> declares
    /// <paramref name="property"/> as a JSON-annotated <see cref="string"/>, the sibling that names
    /// the schema or standard an untyped envelope's shape belongs to.
    /// </summary>
    internal static bool IsSchemaReference(Type contract, string property) =>
        SerializedContracts.DeclaredProperties(contract).SingleOrDefault(p => p.Name == property) is { } sibling
        && sibling.PropertyType == typeof(string)
        && SerializedContracts.IsJsonAnnotated(sibling);

    /// <summary>A property the serializer always skips is not on the wire, so it is not a payload.</summary>
    private static bool IsSerialized(PropertyInfo property) =>
        property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always;
}
