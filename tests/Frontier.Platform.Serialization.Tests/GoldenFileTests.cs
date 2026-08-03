using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// Byte-stability test (canonical-serialization, doc 01 ADR-C1): the wire bytes
/// for a smart enum must never change for a style preference.
/// </summary>
public sealed class GoldenFileTests
{
    [Fact]
    public void Serialize_ExampleStatus_MatchesGoldenFile()
    {
        var provider = new ServiceCollection().AddFrontierSerialization().BuildServiceProvider();
        var options = provider.GetRequiredService<JsonSerializerOptions>();

        var actual = JsonSerializer.SerializeToUtf8Bytes(ExampleStatus.InProgress, options);
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", "example_status.json"));

        Assert.Equal(expected, actual);
    }
}
