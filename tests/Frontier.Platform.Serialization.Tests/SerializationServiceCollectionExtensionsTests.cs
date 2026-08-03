using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Serialization.Tests;

public sealed class SerializationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFrontierSerialization_RegistersJsonSerializerOptionsSingleton()
    {
        var services = new ServiceCollection().AddFrontierSerialization();
        var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<JsonSerializerOptions>();
        var second = provider.GetRequiredService<JsonSerializerOptions>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFrontierSerialization_RegisteredOptions_OmitNullsOnWrite()
    {
        var provider = new ServiceCollection().AddFrontierSerialization().BuildServiceProvider();
        var options = provider.GetRequiredService<JsonSerializerOptions>();

        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
    }

    [Fact]
    public void AddFrontierSerialization_RegisteredOptions_SerializeSmartEnumAsCanonicalName()
    {
        var provider = new ServiceCollection().AddFrontierSerialization().BuildServiceProvider();
        var options = provider.GetRequiredService<JsonSerializerOptions>();

        Assert.Equal("\"in_progress\"", JsonSerializer.Serialize(ExampleStatus.InProgress, options));
    }

    [Fact]
    public void AddFrontierSerialization_RegistersCanonicalProfileCheckAsStartupCheck()
    {
        var provider = new ServiceCollection().AddFrontierSerialization().BuildServiceProvider();

        var check = provider.GetRequiredService<IStartupCheck>();

        Assert.IsType<CanonicalProfileCheck>(check);
    }
}
