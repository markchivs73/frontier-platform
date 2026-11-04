using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// S13.210: finds the keys in a JSON document that the target type's contract does not define.
/// <para>
/// The canonical profile sets no <c>UnmappedMemberHandling</c>, so the serializer drops an unknown
/// key without a word, and a key the designer model misspelled or invented vanishes on parse. The
/// profile is not forked to change that (K10, <c>CanonicalProfileForkTests</c>): the document is
/// walked against the profile's own <see cref="JsonTypeInfo"/> metadata instead, so what counts as
/// "defined" is exactly what the serializer would bind — wire names, polymorphic derived types
/// chosen by their discriminator, collection elements and dictionary values.
/// </para>
/// <para>
/// A type with a custom converter (<see cref="JsonTypeInfoKind.None"/>) is opaque: its shape is the
/// converter's business, so nothing inside it is reported. Dictionary keys are data, not schema.
/// </para>
/// </summary>
internal static class UnknownJsonKeys
{
    /// <summary>
    /// Every key in <paramref name="element"/> that <paramref name="type"/>'s contract does not
    /// define, as a path from the root (<c>$.definition.nodes[0].timout</c>), in document order.
    /// </summary>
    internal static IReadOnlyList<string> Find(JsonElement element, Type type, JsonSerializerOptions options)
    {
        var found = new List<string>();
        Walk(element, options.GetTypeInfo(type), "$", found);
        return found;
    }

    private static void Walk(JsonElement element, JsonTypeInfo info, string path, List<string> found)
    {
        switch (info.Kind)
        {
            case JsonTypeInfoKind.Object when element.ValueKind == JsonValueKind.Object:
                WalkObject(element, info, path, found);
                break;
            case JsonTypeInfoKind.Enumerable when element.ValueKind == JsonValueKind.Array:
                WalkArray(element, info.Options.GetTypeInfo(info.ElementType!), path, found);
                break;
            case JsonTypeInfoKind.Dictionary when element.ValueKind == JsonValueKind.Object:
                WalkDictionary(element, info.Options.GetTypeInfo(info.ElementType!), path, found);
                break;
        }
    }

    private static void WalkObject(JsonElement element, JsonTypeInfo declared, string path, List<string> found)
    {
        var discriminator = declared.PolymorphismOptions?.TypeDiscriminatorPropertyName;
        var info = ResolveDerived(element, declared);
        var acceptsAny = info.Properties.Any(p => p.IsExtensionData);
        foreach (var member in element.EnumerateObject())
        {
            var memberPath = $"{path}.{member.Name}";
            var property = info.Properties.FirstOrDefault(p => p.Name == member.Name && !p.IsExtensionData);
            if (property is not null)
                Walk(member.Value, info.Options.GetTypeInfo(property.PropertyType), memberPath, found);
            else if (member.Name != discriminator && !acceptsAny)
                found.Add(memberPath);
        }
    }

    private static void WalkArray(JsonElement element, JsonTypeInfo elementInfo, string path, List<string> found)
    {
        var index = 0;
        foreach (var item in element.EnumerateArray())
            Walk(item, elementInfo, $"{path}[{index++}]", found);
    }

    private static void WalkDictionary(JsonElement element, JsonTypeInfo valueInfo, string path, List<string> found)
    {
        foreach (var entry in element.EnumerateObject())
            Walk(entry.Value, valueInfo, $"{path}.{entry.Name}", found);
    }

    /// <summary>
    /// The derived type a polymorphic object's discriminator selects, or <paramref name="declared"/>
    /// itself when the type is not polymorphic or the tag is absent or unrecognised (the serializer
    /// rejects those cases on its own, so this walk need not).
    /// </summary>
    internal static JsonTypeInfo ResolveDerived(JsonElement element, JsonTypeInfo declared)
    {
        var polymorphism = declared.PolymorphismOptions;
        if (polymorphism is null
            || !element.TryGetProperty(polymorphism.TypeDiscriminatorPropertyName, out var tag)
            || tag.ValueKind != JsonValueKind.String)
            return declared;

        var match = polymorphism.DerivedTypes.FirstOrDefault(d => d.TypeDiscriminator as string == tag.GetString());
        return match.DerivedType is null ? declared : declared.Options.GetTypeInfo(match.DerivedType);
    }
}
