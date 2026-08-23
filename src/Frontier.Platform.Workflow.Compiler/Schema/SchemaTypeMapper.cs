using Frontier.Platform.Abstractions;


namespace Frontier.Platform.Workflow.Compiler.Schema;

/// <summary>
/// Maps CLR property types to the wire type tokens used in <see cref="FieldDescriptor.Type"/>
/// (doc 14 §7). Tokens are deliberately coarse — the design agent needs to know a field is a
/// string, an integer, a list, an enum (with its value list), or a named complex object; the
/// internal shape of complex contracts is out of scope for S9.7 (referenced by name only).
/// </summary>
internal static class SchemaTypeMapper
{
    /// <summary>Returns the wire type token for a property's CLR type.</summary>
    internal static string MapToken(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return "string";
        if (t == typeof(int) || t == typeof(long)) return "integer";
        if (t == typeof(bool)) return "boolean";
        if (IsStringList(t)) return "array<string>";
        if (IsSmartEnum(t)) return $"enum:{t.Name}";
        return $"object:{t.Name}";
    }

    /// <summary>
    /// True if the type maps to an <c>object:&lt;Name&gt;</c> token — a complex contract whose fields
    /// should be expanded into the schema's <c>objects</c> section. Excludes primitives, smart enums,
    /// and any collection.
    /// </summary>
    internal static bool TryGetComplexType(Type type, out Type complexType)
    {
        complexType = Nullable.GetUnderlyingType(type) ?? type;
        if (complexType == typeof(string) || complexType == typeof(int) || complexType == typeof(long) || complexType == typeof(bool))
            return false;
        if (IsSmartEnum(complexType)) return false;
        return !typeof(System.Collections.IEnumerable).IsAssignableFrom(complexType);
    }

    /// <summary>True if the type is an <see cref="IEnumerable{T}"/> of <see cref="string"/> (but not a string itself).</summary>
    internal static bool IsStringList(Type t) =>
        t != typeof(string) && typeof(IEnumerable<string>).IsAssignableFrom(t);

    /// <summary>True if the type derives from <c>SmartEnum&lt;TEnum&gt;</c> (a domain enum with a value list).</summary>
    internal static bool IsSmartEnum(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
        {
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(SmartEnum<>)) return true;
        }

        return false;
    }
}
