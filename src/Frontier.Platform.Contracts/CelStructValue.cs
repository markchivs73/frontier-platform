using Celly.Providers;
using Celly.Types;
using Celly.Values;

namespace Frontier.Platform.Contracts;

/// <summary>
/// A payload object typed as a schema struct. Only declared properties are fields. A property the
/// payload leaves out is absent: <c>has()</c> is false and selecting it is an error, as Kubernetes
/// treats JSON objects in CEL.
/// </summary>
internal sealed class CelStructValue(CelType type, IReadOnlyDictionary<string, CelValue> fields) : CelValue, IStructValue
{
    /// <inheritdoc />
    public override CelType Type => type;

    /// <inheritdoc />
    public CelValue GetField(string name) => fields.TryGetValue(name, out var value) ? value : ErrorValue.NoSuchAttribute(name);

    /// <inheritdoc />
    public CelValue HasField(string name) => BoolValue.Of(fields.ContainsKey(name));

    /// <inheritdoc />
    public override bool EqualTo(CelValue other) =>
        other is CelStructValue struct2
        && struct2.Type.Name == type.Name
        && struct2.Fields.Count == fields.Count
        && fields.All(field => struct2.Fields.TryGetValue(field.Key, out var value) && field.Value.EqualTo(value));

    /// <inheritdoc />
    public override object ToNative() => fields.ToDictionary(field => field.Key, field => field.Value.ToNative(), StringComparer.Ordinal);

    /// <summary>The fields present on this value.</summary>
    internal IReadOnlyDictionary<string, CelValue> Fields => fields;
}
