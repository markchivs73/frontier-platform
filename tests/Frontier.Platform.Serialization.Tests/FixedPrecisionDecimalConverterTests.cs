using System.Text;
using System.Text.Json;

namespace Frontier.Platform.Serialization.Tests;

public sealed class FixedPrecisionDecimalConverterTests
{
    [Fact]
    public void Serialize_DefaultScale_WritesFourDecimalPlaces()
    {
        var json = JsonSerializer.Serialize(1.5m, CanonicalProfile.Options);

        Assert.Equal("\"1.5000\"", json);
    }

    [Fact]
    public void Serialize_DeclaredScaleTwo_WritesTwoDecimalPlaces()
    {
        var converter = new FixedPrecisionDecimalConverter(scale: 2);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, 1250m, CanonicalProfile.Options);
        writer.Flush();

        Assert.Equal("\"1250.00\"", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Write_NullWriter_Throws()
    {
        var converter = new FixedPrecisionDecimalConverter();

        Assert.Throws<ArgumentNullException>(() => converter.Write(null!, 1m, CanonicalProfile.Options));
    }

    [Fact]
    public void Deserialize_CanonicalString_RoundTrips()
    {
        var value = JsonSerializer.Deserialize<decimal>("\"1250.00\"", CanonicalProfile.Options);

        Assert.Equal(1250.00m, value);
    }

    [Fact]
    public void Deserialize_NumberToken_Parses()
    {
        var value = JsonSerializer.Deserialize<decimal>("1250.00", CanonicalProfile.Options);

        Assert.Equal(1250.00m, value);
    }

    [Fact]
    public void Read_NullToken_Throws()
    {
        Assert.Throws<JsonException>(ReadNullToken);
    }

    internal static void ReadNullToken()
    {
        var converter = new FixedPrecisionDecimalConverter();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("null"));
        reader.Read();

        converter.Read(ref reader, typeof(decimal), CanonicalProfile.Options);
    }
}
