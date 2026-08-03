using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Canonical date/time wire format (doc 01 §3.2): <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> —
/// always UTC, always exactly millisecond precision (a fixed-width fractional part
/// avoids the variable-digit byte-instability bug). <see cref="DateTimeOffset"/> is
/// banned in contracts; values are normalised to UTC <see cref="DateTime"/> at the edge.
/// </summary>
public sealed class Iso8601UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <inheritdoc />
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.ParseExact(
            reader.GetString() ?? throw new JsonException("Expected a string value for an ISO-8601 UTC date/time."),
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value.Kind == DateTimeKind.Unspecified)
        {
            throw new JsonException($"{nameof(DateTime)} with {nameof(DateTimeKind.Unspecified)} kind cannot be serialized; normalise to UTC first.");
        }

        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}
