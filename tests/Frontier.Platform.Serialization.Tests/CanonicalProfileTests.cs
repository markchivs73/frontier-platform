using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization.Tests;

public sealed class CanonicalProfileTests
{
    [Fact]
    public void Options_IsReadOnly()
    {
        Assert.True(CanonicalProfile.Options.IsReadOnly);
    }

    [Fact]
    public void Options_OmitsNullAndUsesStrictNumberHandling()
    {
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, CanonicalProfile.Options.DefaultIgnoreCondition);
        Assert.Equal(JsonNumberHandling.Strict, CanonicalProfile.Options.NumberHandling);
        Assert.Null(CanonicalProfile.Options.PropertyNamingPolicy);
        Assert.False(CanonicalProfile.Options.WriteIndented);
    }

    [Fact]
    public void SerializeCanonical_OmitsNullProperties()
    {
        var bytes = CanonicalProfile.SerializeCanonical(new Sample { Name = "scope", Description = null });

        Assert.Equal("{\"name\":\"scope\"}", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Hash_IsStableForIdenticalValues()
    {
        var first = CanonicalProfile.Hash(new Sample { Name = "scope", Description = null });
        var second = CanonicalProfile.Hash(new Sample { Name = "scope", Description = null });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Hash_DiffersForDifferentValues()
    {
        var first = CanonicalProfile.Hash(new Sample { Name = "scope", Description = null });
        var second = CanonicalProfile.Hash(new Sample { Name = "approach", Description = null });

        Assert.NotEqual(first, second);
    }

    private sealed record Sample
    {
        [JsonPropertyOrder(0)]
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyOrder(1)]
        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
