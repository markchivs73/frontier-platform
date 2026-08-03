using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>S6.5 coverage: round-trip and guard-clause behaviour of <see cref="EngagementIdConverter"/>.</summary>
public sealed class EngagementIdConverterTests
{
    [Fact]
    public void Read_ValidString_ReturnsEngagementId()
    {
        var json = "\"eng-42\"";

        var result = JsonSerializer.Deserialize<EngagementId>(json, CanonicalProfile.Options);

        Assert.Equal("eng-42", result!.Value);
    }

    [Fact]
    public void Write_ValidEngagementId_WritesStringValue()
    {
        var id = new EngagementId("eng-99");

        var json = JsonSerializer.Serialize(id, CanonicalProfile.Options);

        Assert.Equal("\"eng-99\"", json);
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        var original = new EngagementId("eng-round-trip");

        var json = JsonSerializer.Serialize(original, CanonicalProfile.Options);
        var deserialized = JsonSerializer.Deserialize<EngagementId>(json, CanonicalProfile.Options);

        Assert.Equal(original, deserialized);
    }

    [Fact]
    public void Read_NullJsonToken_ThrowsJsonException()
    {
        // JsonSerializer handles null tokens before calling converters, so invoke the converter directly.
        var converter = new EngagementIdConverter();
        var bytes = Encoding.UTF8.GetBytes("null");
        var reader = new Utf8JsonReader(bytes);
        reader.Read();

        var caught = false;
        try { converter.Read(ref reader, typeof(EngagementId), CanonicalProfile.Options); }
        catch (JsonException) { caught = true; }
        Assert.True(caught);
    }

    [Fact]
    public void Write_NullWriter_ThrowsArgumentNullException()
    {
        var converter = new EngagementIdConverter();
        var id = new EngagementId("eng-1");

        Assert.Throws<ArgumentNullException>(() =>
            converter.Write(null!, id, CanonicalProfile.Options));
    }

    [Fact]
    public void Write_NullValue_ThrowsArgumentNullException()
    {
        var converter = new EngagementIdConverter();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        Assert.Throws<ArgumentNullException>(() =>
            converter.Write(writer, null!, CanonicalProfile.Options));
    }
}
