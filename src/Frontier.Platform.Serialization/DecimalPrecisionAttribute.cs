using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Declares the wire scale for a <see cref="decimal"/> contract property (doc 01 §3.3),
/// e.g. <c>[DecimalPrecision(2)]</c> for money fields. Resolves to a
/// <see cref="FixedPrecisionDecimalConverter"/> with the declared scale, overriding the
/// default-scale-4 converter registered globally on <see cref="CanonicalProfile"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DecimalPrecisionAttribute : JsonConverterAttribute
{
    /// <summary>Declares the number of digits written after the decimal point.</summary>
    public DecimalPrecisionAttribute(int scale)
    {
        Scale = scale;
    }

    /// <summary>The number of digits written after the decimal point.</summary>
    public int Scale { get; }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert) => new FixedPrecisionDecimalConverter(Scale);
}
