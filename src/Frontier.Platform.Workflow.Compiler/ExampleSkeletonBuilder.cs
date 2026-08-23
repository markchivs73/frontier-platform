using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Builds a valid example instance of an <c>IVersionedContract</c>-shaped CLR type by
/// reflection, then serializes it through the real canonical profile
/// (<see cref="CanonicalProfile.Options"/>) — every placeholder value (decimals, dates, smart
/// enums) comes out in its actual wire form, via the same converters
/// <c>IContractTypeRegistry.DeserializeAndValidate</c> uses at execution time. Backs
/// <see cref="TestRunInputSchemaProvider"/>'s "Use example" prefill (S9.43, doc 19 §A4-R2/C-31).
/// A property already holding a non-default value after construction (e.g. a property
/// initializer like <c>SchemaVersion = "1.0"</c>) is left untouched; only properties left at
/// their CLR default (i.e. <c>required</c> properties, which have no initializer) get a
/// placeholder — this needs no <c>required</c>-specific reflection at all.
/// </summary>
internal static class ExampleSkeletonBuilder
{
    private const int MaxDepth = 6;

    /// <summary>Builds the canonical-wire-form example JSON for <paramref name="contractType"/>.</summary>
    internal static JsonNode Build(Type contractType)
    {
        var instance = CreateInstance(contractType, depth: 0);
        return JsonSerializer.SerializeToNode(instance, contractType, CanonicalProfile.Options)!;
    }

    /// <summary>Constructs <paramref name="type"/> via its parameterless constructor and fills its default-valued properties.</summary>
    internal static object? CreateInstance(Type type, int depth)
    {
        if (depth > MaxDepth || type.GetConstructor(Type.EmptyTypes) is null) return null;

        var instance = Activator.CreateInstance(type)!;
        PopulateDefaultProperties(instance, type, depth);
        return instance;
    }

    /// <summary>Overwrites every property still at its CLR default (untouched by a property initializer) with a placeholder.</summary>
    internal static void PopulateDefaultProperties(object instance, Type type, int depth)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var current = property.GetValue(instance);
            if (current is DateTime dt && dt == default)
            {
                property.SetValue(instance, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }
            else if (current is null)
            {
                property.SetValue(instance, PlaceholderFor(property.PropertyType, depth + 1));
            }
        }
    }

    /// <summary>Builds a placeholder value for a reference-typed (or boxed-null nullable) property type.</summary>
    internal static object? PlaceholderFor(Type type, int depth)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        // A non-empty placeholder, not "": several contracts' Validate() reject an empty
        // required string (e.g. a contract's first required string property), and an example that fails
        // the contract's own validation on first Run defeats the point of "valid skeleton".
        if (t == typeof(string)) return "example";
        if (IsSmartEnum(t)) return FirstSmartEnumValue(t);
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        if (TryGetDictionaryValueType(t, out var valueType)) return BuildDictionary(valueType, depth);
        if (TryGetEnumerableElementType(t, out var elementType)) return BuildList(elementType, depth);
        return t.IsClass ? CreateInstance(t, depth) : Activator.CreateInstance(t);
    }

    /// <summary>A one-entry <c>Dictionary&lt;string, TValue&gt;</c> example.</summary>
    internal static IDictionary BuildDictionary(Type valueType, int depth)
    {
        var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
        dict["key"] = PlaceholderFor(valueType, depth);
        return dict;
    }

    /// <summary>A one-element <c>List&lt;TElement&gt;</c> example, showing the item shape.</summary>
    internal static IList BuildList(Type elementType, int depth)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        list.Add(PlaceholderFor(elementType, depth));
        return list;
    }

    /// <summary>Resolves the value type of a <c>string</c>-keyed dictionary interface/type, if <paramref name="type"/> is one.</summary>
    internal static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        var dictInterface = type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType
                && (i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                && i.GetGenericArguments()[0] == typeof(string));
        valueType = dictInterface?.GetGenericArguments()[1] ?? typeof(object);
        return dictInterface is not null;
    }

    /// <summary>Resolves the element type of an array/<c>IEnumerable&lt;T&gt;</c>, if <paramref name="type"/> is one (excluding <see cref="string"/>, checked by the caller).</summary>
    internal static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumInterface = type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        elementType = enumInterface?.GetGenericArguments()[0] ?? typeof(object);
        return enumInterface is not null;
    }

    /// <summary>Duck-type check for a domain smart enum: a public instance <c>Name</c> string property plus a static <c>FromName(string)</c> method.</summary>
    internal static bool IsSmartEnum(Type type) =>
        type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string)
        && type.GetMethod("FromName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(string)]) is not null;

    /// <summary>The first declared value of a smart enum type, via its static <c>List</c> property.</summary>
    internal static object FirstSmartEnumValue(Type type)
    {
        var list = (IEnumerable)type.GetProperty("List", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)!;
        return list.Cast<object>().First();
    }
}
