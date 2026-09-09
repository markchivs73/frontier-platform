using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization.Tests;

/// <summary>
/// ADR-PA19: the canonical profile reads a polymorphic document whatever order its keys arrive in,
/// and still writes the discriminator first. A store that reorders keys on the way back (the
/// 2026-09 Cosmos emulator's query path did) must not be able to break a read.
/// </summary>
public sealed class PolymorphicKeyOrderTests
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Leaf), "leaf")]
    [JsonDerivedType(typeof(Branch), "branch")]
    private abstract record Node
    {
        [JsonPropertyOrder(0)] [JsonPropertyName("id")] public required string Id { get; init; }
    }

    private sealed record Leaf : Node
    {
        [JsonPropertyOrder(1)] [JsonPropertyName("value")] public int Value { get; init; }
    }

    private sealed record Branch : Node
    {
        [JsonPropertyOrder(1)] [JsonPropertyName("children")] public IReadOnlyList<Node> Children { get; init; } = [];
    }

    private static readonly Node SampleGraph = new Branch { Id = "root", Children = [new Leaf { Id = "a", Value = 1 }, new Branch { Id = "b", Children = [new Leaf { Id = "c", Value = 3 }] }] };

    /// <summary>The canonical profile with only ADR-PA19's flag turned back off — the control differs from the real options in exactly the thing under test.</summary>
    private static readonly JsonSerializerOptions ProfileWithoutOutOfOrderMetadata = new(CanonicalProfile.Options) { AllowOutOfOrderMetadataProperties = false };

    /// <summary>Moves the first property of every object to the end, recursively — the adversarial reorder, since the profile writes the discriminator first.</summary>
    internal static JsonNode RotateEveryObject(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var pairs = obj.Select(p => (p.Key, Value: p.Value is null ? null : RotateEveryObject(p.Value))).ToList();
                foreach (var key in pairs.Select(p => p.Key)) obj.Remove(key);
                foreach (var (key, value) in pairs.Skip(1).Concat(pairs.Take(1))) obj[key] = value;
                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) if (arr[i] is { } child) { var rotated = RotateEveryObject(child); if (!ReferenceEquals(rotated, child)) arr[i] = rotated; }
                return arr;
            default:
                return node;
        }
    }

    [Fact]
    public void Write_PutsTheDiscriminatorFirst_AtEveryLevel()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(SampleGraph));

        Assert.StartsWith("{\"kind\":\"branch\"", json, StringComparison.Ordinal);
        Assert.Contains("{\"kind\":\"leaf\",\"id\":\"a\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_DiscriminatorLastAtEveryLevel_DeserialisesTheSameGraph()
    {
        var canonical = CanonicalProfile.SerializeCanonical(SampleGraph);
        var rotated = RotateEveryObject(JsonNode.Parse(canonical)!).ToJsonString();
        Assert.DoesNotContain("{\"kind\"", rotated, StringComparison.Ordinal);

        var read = JsonSerializer.Deserialize<Node>(rotated, CanonicalProfile.Options);

        Assert.NotNull(read);
        Assert.Equal(canonical, CanonicalProfile.SerializeCanonical(read));
    }

    [Fact]
    public void Read_DiscriminatorLast_WouldThrowUnderProfileWithoutOutOfOrderMetadata()
    {
        var rotated = RotateEveryObject(JsonNode.Parse(CanonicalProfile.SerializeCanonical(SampleGraph))!).ToJsonString();

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<Node>(rotated, ProfileWithoutOutOfOrderMetadata));
    }
}
