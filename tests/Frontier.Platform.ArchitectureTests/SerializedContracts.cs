using System.Reflection;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// The serialized contracts the platform architecture suite governs: every type in a platform
/// library that participates in JSON. The platform mirror of the consumer repository's
/// <c>SerializedContracts</c> (S13.213), used by <see cref="UntypedPayloadTests"/> (S13.236).
/// </summary>
internal static class SerializedContracts
{
    /// <summary>Every type in the platform libraries that <see cref="IsSerializedContract"/> accepts.</summary>
    internal static IReadOnlyList<Type> Types() =>
        [.. GovernedAssemblies.PlatformLibraryNames
            .SelectMany(name => Assembly.Load(name).GetTypes())
            .Where(IsSerializedContract)];

    /// <summary>
    /// A type participates in JSON only if one of its own public instance properties carries a
    /// <see cref="JsonPropertyNameAttribute"/> or <see cref="JsonPropertyOrderAttribute"/>;
    /// in-memory construction contracts are left alone.
    /// </summary>
    internal static bool IsSerializedContract(Type type) =>
        DeclaredProperties(type).Any(IsJsonAnnotated);

    /// <summary>The type's own public instance properties, the ones its JSON shape declares.</summary>
    internal static PropertyInfo[] DeclaredProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    /// <summary>True when the property carries a JSON name or order, so it is part of a declared wire shape.</summary>
    internal static bool IsJsonAnnotated(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null
        || property.GetCustomAttribute<JsonPropertyOrderAttribute>() is not null;
}
