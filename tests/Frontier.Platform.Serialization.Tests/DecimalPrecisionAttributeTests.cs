using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization.Tests;

public sealed class DecimalPrecisionAttributeTests
{
    [Fact]
    public void CreateConverter_ReturnsConverterWithDeclaredScale()
    {
        var attribute = new DecimalPrecisionAttribute(2);

        var converter = Assert.IsType<FixedPrecisionDecimalConverter>(attribute.CreateConverter(typeof(decimal)));

        Assert.Equal(2, converter.Scale);
    }

    [Fact]
    public void Serialize_PropertyWithDeclaredScale_OverridesDefaultScale()
    {
        var json = JsonSerializer.Serialize(new MoneyHolder { Amount = 1250m }, CanonicalProfile.Options);

        Assert.Equal("{\"amount\":\"1250.00\"}", json);
    }

    private sealed record MoneyHolder
    {
        [JsonPropertyOrder(0)]
        [JsonPropertyName("amount")]
        [DecimalPrecision(2)]
        public required decimal Amount { get; init; }
    }
}
