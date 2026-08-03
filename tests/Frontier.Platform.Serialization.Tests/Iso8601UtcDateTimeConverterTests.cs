using System.Text;
using System.Text.Json;

namespace Frontier.Platform.Serialization.Tests;

public sealed class Iso8601UtcDateTimeConverterTests
{
    [Fact]
    public void Serialize_UtcValue_WritesMillisecondPrecisionWithZSuffix()
    {
        var value = new DateTime(2026, 6, 12, 0, 34, 0, 123, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(value, CanonicalProfile.Options);

        Assert.Equal("\"2026-06-12T00:34:00.123Z\"", json);
    }

    [Fact]
    public void Serialize_LocalValue_ConvertsToUtcBeforeWriting()
    {
        var local = new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Local);
        var expected = local.ToUniversalTime();

        var json = JsonSerializer.Serialize(local, CanonicalProfile.Options);

        Assert.Equal($"\"{expected:yyyy-MM-ddTHH:mm:ss.fff}Z\"", json);
    }

    [Fact]
    public void Serialize_UnspecifiedKind_Throws()
    {
        var value = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(value, CanonicalProfile.Options));
    }

    [Fact]
    public void Write_NullWriter_Throws()
    {
        var converter = new Iso8601UtcDateTimeConverter();

        Assert.Throws<ArgumentNullException>(() => converter.Write(null!, DateTime.UtcNow, CanonicalProfile.Options));
    }

    [Fact]
    public void Deserialize_CanonicalString_RoundTripsAsUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-06-12T00:34:00.123Z\"", CanonicalProfile.Options);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 6, 12, 0, 34, 0, 123, DateTimeKind.Utc), value);
    }

    [Fact]
    public void Read_NullToken_Throws()
    {
        Assert.Throws<JsonException>(ReadNullToken);
    }

    internal static void ReadNullToken()
    {
        var converter = new Iso8601UtcDateTimeConverter();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("null"));
        reader.Read();

        converter.Read(ref reader, typeof(DateTime), CanonicalProfile.Options);
    }
}
