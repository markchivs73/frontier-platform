using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Canonical decimal wire format (doc 01 §3.3): writes decimals as JSON strings with
/// invariant culture and a fixed scale, so trailing zeros are stable (<c>"1250.00"</c>,
/// never <c>1250</c> vs <c>1250.0</c>). Applied per-property via
/// <see cref="DecimalPrecisionAttribute"/>, or registered globally with the default
/// scale of 4 for properties without an explicit attribute.
/// </summary>
public sealed class FixedPrecisionDecimalConverter : JsonConverter<decimal>
{
    /// <summary>Creates a converter using the default scale (4) — effort/ratio fields.</summary>
    public FixedPrecisionDecimalConverter()
        : this(scale: 4)
    {
    }

    /// <summary>Creates a converter using the given <paramref name="scale"/> — money fields use 2.</summary>
    public FixedPrecisionDecimalConverter(int scale)
    {
        Scale = scale;
    }

    /// <summary>The number of digits written after the decimal point.</summary>
    public int Scale { get; }

    /// <summary>
    /// Reads the canonical string form (<c>"1250.00"</c>), or — since LLM-produced JSON
    /// (S4.2 agent output, not subject to the canonical write side) commonly emits
    /// unquoted decimals — a bare JSON number.
    /// </summary>
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => decimal.Parse(reader.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal(),
            _ => throw new JsonException("Expected a string or number value for a fixed-precision decimal."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString("F" + Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }
}
