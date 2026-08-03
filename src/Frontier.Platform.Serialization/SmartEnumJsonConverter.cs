using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Canonical snake_case string converter for smart enums (TD-11, doc 01 ADR-C1):
/// reads/writes the <c>Name</c> property of any type shaped like
/// <c>Frontier.Reason.Workflow.Abstractions.SmartEnum&lt;TEnum&gt;</c> — a public
/// instance <c>string Name</c> property and a public static
/// <c>TEnum FromName(string)</c> resolver — via reflection, so this assembly never
/// references Abstractions (library-boundaries: Serialization has zero Frontier
/// dependencies). Unknown wire values throw rather than silently coercing.
/// </summary>
public sealed class SmartEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : notnull
{
    private static readonly PropertyInfo NameProperty = ResolveNameProperty();
    private static readonly MethodInfo FromNameMethod = ResolveFromNameMethod();

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var name = reader.GetString();
        try
        {
            return (TEnum)FromNameMethod.Invoke(null, [name])!;
        }
        catch (TargetInvocationException ex)
        {
            // JsonException (not the reflected ArgumentOutOfRangeException) so JSON
            // pipelines treat an unknown wire value as malformed input — ASP.NET Core
            // model binding then answers 400, not 500. Never silently coerces.
            throw new JsonException($"'{name}' is not a valid {typeof(TEnum).Name} wire value.", ex.InnerException);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue((string)NameProperty.GetValue(value)!);
    }

    internal static PropertyInfo ResolveNameProperty() =>
        typeof(TEnum).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{typeof(TEnum).Name} has no public 'Name' property.");

    internal static MethodInfo ResolveFromNameMethod() =>
        typeof(TEnum).GetMethod("FromName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy, [typeof(string)])
            ?? throw new InvalidOperationException($"{typeof(TEnum).Name} has no public static 'FromName(string)' method.");
}
