using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Serializes <see cref="EngagementId"/> as a plain string (not an object),
/// maintaining canonical wire format compatibility.
/// </summary>
public sealed class EngagementIdConverter : JsonConverter<EngagementId>
{
    /// <inheritdoc />
    public override EngagementId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Expected a string value for EngagementId.");
        return new EngagementId(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EngagementId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Value);
    }
}
