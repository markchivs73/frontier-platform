using System.Text.Json;

using Celly.Types;
using Celly.Values;

namespace Frontier.Platform.Contracts;

/// <summary>
/// Turns a payload into CEL values, guided by the schema's types. A struct-typed object becomes a
/// <see cref="CelStructValue"/>; any other object becomes a map whose keys are in ordinal order, so
/// no rule's result can depend on the order a provider wrote them in (ADR-E27 D2). Every number is a
/// <c>double</c>.
/// </summary>
internal static class CelPayload
{
    /// <summary>The CEL value of <paramref name="value"/>, described by the subschema at <paramref name="pointer"/> (null when undescribed).</summary>
    internal static CelValue Convert(CelSchemaTypes types, JsonElement value, string? pointer) => value.ValueKind switch
    {
        JsonValueKind.Object => ConvertObject(types, value, pointer),
        JsonValueKind.Array => ListValue.Of([.. value.EnumerateArray().Select(item => Convert(types, item, Items(types, pointer)))]),
        JsonValueKind.String => StringValue.Of(value.GetString()!),
        JsonValueKind.Number => DoubleValue.Of(value.GetDouble()),
        JsonValueKind.True => BoolValue.True,
        JsonValueKind.False => BoolValue.False,
        _ => NullValue.Instance,
    };

    private static CelValue ConvertObject(CelSchemaTypes types, JsonElement value, string? pointer)
    {
        var type = pointer is null ? CelType.Dyn : types.TypeAt(pointer);
        var members = value.EnumerateObject()
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => (member.Name, Field: pointer is null ? null : types.FieldPointer(pointer, member.Name), member.Value))
            .ToList();
        if (type.Kind == CelTypeKind.Struct)
        {
            return new CelStructValue(type, members
                .Where(member => member.Field is not null)
                .ToDictionary(member => member.Name, member => Convert(types, member.Value, member.Field), StringComparer.Ordinal));
        }

        return MapValue.Build(members.Select(member =>
            new KeyValuePair<CelValue, CelValue>(StringValue.Of(member.Name), Convert(types, member.Value, member.Field))));
    }

    private static string? Items(CelSchemaTypes types, string? pointer) =>
        pointer is not null && types.TypeAt(pointer).Kind == CelTypeKind.List ? types.ElementPointer(pointer) : null;
}
