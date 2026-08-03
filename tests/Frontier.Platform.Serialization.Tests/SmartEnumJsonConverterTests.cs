using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Serialization.Tests;

public sealed class SmartEnumJsonConverterTests
{
    [Fact]
    public void Serialize_KnownValue_WritesCanonicalName()
    {
        Assert.Equal("\"in_progress\"", JsonSerializer.Serialize(ExampleStatus.InProgress, ResolveOptions()));
    }

    [Fact]
    public void Deserialize_KnownName_ReturnsMatchingValue()
    {
        Assert.Same(ExampleStatus.InProgress, JsonSerializer.Deserialize<ExampleStatus>("\"in_progress\"", ResolveOptions()));
    }

    [Fact]
    public void Deserialize_UnknownName_ThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ExampleStatus>("\"unknown\"", ResolveOptions()));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void Serialize_TypeWithoutNameProperty_ThrowsTypeInitializationException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SmartEnumJsonConverter<int>());

        Assert.Throws<TypeInitializationException>(() => JsonSerializer.Serialize(0, options));
    }

    [Fact]
    public void Deserialize_TypeWithoutFromNameMethod_ThrowsTypeInitializationException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SmartEnumJsonConverter<TypeWithNameButNoFromName>());

        Assert.Throws<TypeInitializationException>(() => JsonSerializer.Deserialize<TypeWithNameButNoFromName>("\"x\"", options));
    }

    private static JsonSerializerOptions ResolveOptions() =>
        new ServiceCollection().AddFrontierSerialization().BuildServiceProvider().GetRequiredService<JsonSerializerOptions>();
}
