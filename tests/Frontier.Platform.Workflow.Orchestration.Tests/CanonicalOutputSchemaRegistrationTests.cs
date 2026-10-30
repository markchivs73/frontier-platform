using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// ADR-E27 D9's exporter check, as ADR-PA41 decided it. A .NET contract is generated with
/// <c>JsonSchemaExporter</c> and then registered, so the export has to say what the canonical
/// profile's converters write: decimals as fixed-scale strings, ISO-8601 dates, engagement ids
/// and smart enums as strings. The probe records carry one property of each kind, a plain enum
/// and a nullable string, once bare and once with a <c>[Description]</c> on every property.
/// </summary>
public sealed class CanonicalOutputSchemaRegistrationTests
{
    private const string ContractRef = "frontier/exporter-probe/1.0";

    [Theory]
    [InlineData(typeof(BareProbe))]
    [InlineData(typeof(DescribedProbe))]
    public void For_EveryConverterOwnedProperty_GetsItsWireSchema(Type probe)
    {
        var properties = Export(probe)["properties"]!;

        Assert.Equal("^-?[0-9]+\\.[0-9]{2}$", properties["price"]!["pattern"]!.GetValue<string>());
        Assert.Equal("^-?[0-9]+\\.[0-9]{4}$", properties["ratio"]!["pattern"]!.GetValue<string>());
        Assert.Contains("ISO-8601", properties["at"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("ISO-8601", properties["maybe_at"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(ExecutionStatus.List.Select(s => s.Name), properties["status"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        foreach (var name in new[] { "price", "ratio", "at", "maybe_at", "engagement_id", "status" })
        {
            Assert.Equal("string", properties[name]!["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public void For_DescribedConverterOwnedProperty_KeepsTheAuthorsDescriptionInFront()
    {
        var properties = Export(typeof(DescribedProbe))["properties"]!;

        Assert.StartsWith("The unit price. Fixed-precision decimal", properties["price"]!["description"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Where the run stands.", properties["status"]!["description"]!.GetValue<string>());
        Assert.Equal("The engagement the price belongs to.", properties["engagement_id"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public void For_DescribedOpaqueTypeItDoesNotRecognise_LeavesTheNodeForTheSweep()
    {
        var opaque = Export(typeof(DescribedOpaqueProbe))["properties"]!["opaque"]!;

        Assert.Equal("""{"description":"Something only its converter understands."}""", opaque.ToJsonString());
    }

    [Theory]
    [InlineData("""{"description":5}""")]
    [InlineData("""{"title":"x"}""")]
    [InlineData("""{"description":"x","type":"string"}""")]
    [InlineData("true")]
    public void DescriptionOnly_AnythingButALoneTextDescription_IsNull(string node) =>
        Assert.Null(CanonicalOutputSchema.DescriptionOnly(JsonNode.Parse(node)!));

    [Theory]
    [InlineData(typeof(BareProbe))]
    [InlineData(typeof(DescribedProbe))]
    public void Check_RawExport_IsRefusedAsAGeneratedContract(Type probe)
    {
        var result = Check(Export(probe));

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Findings, f => f.Code == ContractFindingCodes.DescriptionMissing);
        Assert.Contains(result.Findings, f => f.Code == ContractFindingCodes.ExamplesMissing);
        Assert.Contains(result.Findings, f => f.Code == ContractFindingCodes.ContentModelUnstated);
    }

    [Fact]
    public void Check_DescribedExportWithWhatAGeneratorAdds_IsAcceptedWithNoFindings()
    {
        var result = Check(Registrable(typeof(DescribedProbe), DescribedProbe.Example));

        Assert.Empty(result.Findings);
        Assert.NotNull(result.Snapshot);
    }

    /// <summary>
    /// What a generator must add beyond the export, recorded precisely: a description on every
    /// property the exporter cannot describe from its wire form. The decimals and dates already carry
    /// their wire description; the engagement id, the enums and the string do not.
    /// </summary>
    [Fact]
    public void Check_BareExportWithWhatAGeneratorAdds_IsRefusedOnlyForPropertyDescriptions()
    {
        var result = Check(Registrable(typeof(BareProbe), BareProbe.Example));

        Assert.Null(result.Snapshot);
        Assert.All(result.Findings, f => Assert.Equal(ContractFindingCodes.PropertyDescriptionMissing, f.Code));
        Assert.Equal(
            ["/schema/properties/colour", "/schema/properties/engagement_id", "/schema/properties/note", "/schema/properties/status"],
            result.Findings.Select(f => f.Location).Order(StringComparer.Ordinal));
    }

    /// <summary>The example is the probe's canonical bytes, so the schema is held to what the profile writes.</summary>
    [Fact]
    public void Check_ExampleThatBreaksTheWireForm_IsRefused()
    {
        var schema = Registrable(typeof(DescribedProbe), DescribedProbe.Example);
        schema["examples"]![0]!["price"] = "12.5";

        var result = Check(schema);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Findings, f => f.Code == ContractFindingCodes.ExampleInvalid);
    }

    private static JsonObject Export(Type type) =>
        JsonNode.Parse(Assert.IsType<ChatResponseFormatJson>(CanonicalOutputSchema.For(type)).Schema!.Value.GetRawText())!.AsObject();

    /// <summary>The export plus what a generator adds: a contract description, a closed object and one example.</summary>
    private static JsonObject Registrable(Type type, object example)
    {
        var schema = Export(type);
        schema["description"] = "A probe of the canonical profile's converter-owned types. Not a real contract.";
        schema["additionalProperties"] = false;
        schema["examples"] = new JsonArray(JsonNode.Parse(CanonicalProfile.SerializeCanonical(example)));
        return schema;
    }

    private static ContractRegistrationResult Check(JsonObject schema)
    {
        using var services = new ServiceCollection()
            .AddFrontierContracts(new ConfigurationBuilder().Build())
            .BuildServiceProvider();
        var request = new ContractRegistrationRequest
        {
            ContractRef = ContractRef,
            Schema = JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString()),
            Provenance = ContractProvenance.Generated,
        };
        return services.GetRequiredService<IContractRegistrationCheck>().Check(request, []);
    }

    internal enum Colour
    {
        Red,
        Blue,
    }

    private sealed record BareProbe
    {
        public static readonly BareProbe Example = new()
        {
            Price = 12.50m,
            Ratio = 0.3333m,
            At = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc),
            EngagementId = new EngagementId("eng-1"),
            Status = ExecutionStatus.Running,
            Colour = Colour.Blue,
        };

        [JsonPropertyName("price")]
        [DecimalPrecision(2)]
        public required decimal Price { get; init; }

        [JsonPropertyName("ratio")]
        public required decimal Ratio { get; init; }

        [JsonPropertyName("at")]
        public required DateTime At { get; init; }

        [JsonPropertyName("maybe_at")]
        public DateTime? MaybeAt { get; init; }

        [JsonPropertyName("engagement_id")]
        public required EngagementId EngagementId { get; init; }

        [JsonPropertyName("status")]
        public required ExecutionStatus Status { get; init; }

        [JsonPropertyName("colour")]
        public required Colour Colour { get; init; }

        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }

    private sealed record DescribedProbe
    {
        public static readonly DescribedProbe Example = new()
        {
            Price = 12.50m,
            Ratio = 0.3333m,
            At = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc),
            MaybeAt = new DateTime(2026, 10, 31, 9, 30, 0, DateTimeKind.Utc),
            EngagementId = new EngagementId("eng-1"),
            Status = ExecutionStatus.Running,
            Colour = Colour.Blue,
            Note = "First quote.",
        };

        [JsonPropertyName("price")]
        [DecimalPrecision(2)]
        [Description("The unit price.")]
        public required decimal Price { get; init; }

        [JsonPropertyName("ratio")]
        [Description("The share of the total.")]
        public required decimal Ratio { get; init; }

        [JsonPropertyName("at")]
        [Description("When the price was set.")]
        public required DateTime At { get; init; }

        [JsonPropertyName("maybe_at")]
        [Description("When the price expires, if it does.")]
        public DateTime? MaybeAt { get; init; }

        [JsonPropertyName("engagement_id")]
        [Description("The engagement the price belongs to.")]
        public required EngagementId EngagementId { get; init; }

        [JsonPropertyName("status")]
        [Description("Where the run stands.")]
        public required ExecutionStatus Status { get; init; }

        [JsonPropertyName("colour")]
        [Description("The colour, as the enum's number.")]
        public required Colour Colour { get; init; }

        [JsonPropertyName("note")]
        [Description("A note for the reviewer.")]
        public string? Note { get; init; }
    }

    private sealed record DescribedOpaqueProbe
    {
        [JsonPropertyName("opaque")]
        [JsonConverter(typeof(OpaqueGuidConverter))]
        [Description("Something only its converter understands.")]
        public required Guid Opaque { get; init; }
    }

    private sealed class OpaqueGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Guid.Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
