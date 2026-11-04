using System.Text.Json;
using System.Text.Json.Serialization;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.210: <see cref="UnknownJsonKeys"/> reports the keys a document carries that the target
/// contract does not define, judged by the canonical profile's own metadata (no forked options).
/// </summary>
public sealed class UnknownJsonKeysTests
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Circle), "circle")]
    internal abstract record Shape
    {
        [JsonPropertyName("label")]
        public string? Label { get; init; }
    }

    internal sealed record Circle : Shape
    {
        [JsonPropertyName("radius")]
        public int Radius { get; init; }
    }

    internal sealed record Holder
    {
        [JsonPropertyName("shapes")]
        public IReadOnlyList<Shape>? Shapes { get; init; }

        [JsonPropertyName("by_name")]
        public IReadOnlyDictionary<string, Shape>? ByName { get; init; }

        [JsonPropertyName("open")]
        public Open? OpenBag { get; init; }

        [JsonPropertyName("free")]
        public JsonElement? Free { get; init; }

        [JsonPropertyName("when")]
        public DateTime? When { get; init; }
    }

    internal sealed record Open
    {
        [JsonPropertyName("known")]
        public Circle? Known { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Rest { get; init; }
    }

    private static IReadOnlyList<string> Find(string json)
    {
        using var document = JsonDocument.Parse(json);
        return UnknownJsonKeys.Find(document.RootElement, typeof(Holder), CanonicalProfile.Options);
    }

    [Fact]
    public void ADocumentOfOnlyDefinedKeys_ReportsNothing() =>
        Assert.Empty(Find("""{"shapes":[{"kind":"circle","label":"a","radius":1}],"when":"2026-01-01T00:00:00.000Z"}"""));

    [Fact]
    public void AnInventedTopLevelKey_IsReportedWithItsPath() =>
        Assert.Equal(["$.notes"], Find("""{"notes":"x"}"""));

    [Fact]
    public void AKeyOnlyTheDerivedTypeDefines_IsKnown_AndAMisspeltOneIsReported() =>
        Assert.Equal(["$.shapes[1].raduis"],
            Find("""{"shapes":[{"kind":"circle","radius":1},{"radius":2,"kind":"circle","raduis":2}]}"""));

    [Fact]
    public void DictionaryKeysAreData_ButTheirValuesAreChecked() =>
        Assert.Equal(["$.by_name.anything.colour"],
            Find("""{"by_name":{"anything":{"kind":"circle","colour":"red"}}}"""));

    [Theory]
    [InlineData("""{"shapes":[{"kind":"square","label":"a","side":1}]}""", "$.shapes[0].side")]
    [InlineData("""{"shapes":[{"kind":7,"label":"a","side":1}]}""", "$.shapes[0].side")]
    [InlineData("""{"shapes":[{"label":"a","side":1}]}""", "$.shapes[0].side")]
    public void AnUnresolvableDiscriminator_FallsBackToTheDeclaredType(string json, string expected) =>
        Assert.Equal([expected], Find(json));

    [Fact]
    public void ATypeWithExtensionData_AcceptsAnyKey_ButStillChecksItsKnownMembers() =>
        Assert.Equal(["$.open.known.edge"],
            Find("""{"open":{"extra":1,"known":{"radius":1,"edge":2}}}"""));

    [Fact]
    public void ConverterOwnedAndMismatchedValues_AreNotWalked() =>
        Assert.Empty(Find("""{"free":{"anything":1},"shapes":{"not":"an array"},"by_name":[1],"open":null,"when":"2026-01-01T00:00:00.000Z"}"""));
}
