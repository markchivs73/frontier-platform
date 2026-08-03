using System.Text.Json;

namespace Frontier.Platform.Serialization.Tests;

public sealed class SmartEnumJsonConverterFactoryTests
{
    private static readonly SmartEnumJsonConverterFactory Factory = new();

    [Fact]
    public void CanConvert_SmartEnumType_ReturnsTrue()
    {
        Assert.True(Factory.CanConvert(typeof(ExampleStatus)));
    }

    [Fact]
    public void CanConvert_TypeWithoutNameProperty_ReturnsFalse()
    {
        Assert.False(Factory.CanConvert(typeof(string)));
    }

    [Fact]
    public void CanConvert_TypeWithNameButNoFromName_ReturnsFalse()
    {
        Assert.False(Factory.CanConvert(typeof(TypeWithNameButNoFromName)));
    }

    [Fact]
    public void CanConvert_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Factory.CanConvert(null!));
    }

    [Fact]
    public void CreateConverter_SmartEnumType_ReturnsSmartEnumJsonConverter()
    {
        var converter = Factory.CreateConverter(typeof(ExampleStatus), new JsonSerializerOptions());

        Assert.IsType<SmartEnumJsonConverter<ExampleStatus>>(converter);
    }
}
