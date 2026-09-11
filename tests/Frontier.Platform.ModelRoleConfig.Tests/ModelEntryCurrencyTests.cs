using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA21: model prices are currency-free amounts beside an ISO 4217 code, seeded at Anthropic's USD list prices.</summary>
public sealed class ModelEntryCurrencyTests
{
    [Fact]
    public void ModelEntryDocument_RoundTripsThroughCanonicalProfile_WithCurrency()
    {
        var document = ModelEntryDocument.FromDomain(Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0]);

        var bytes = CanonicalProfile.SerializeCanonical(document);
        var json = JsonNode.Parse(bytes)!.AsObject();
        var roundTripped = JsonSerializer.Deserialize<ModelEntryDocument>(bytes, CanonicalProfile.Options)!;

        Assert.Equal(["provider", "model_id", "input_cost_per_1k", "output_cost_per_1k", "cache_read_cost_per_1k", "currency", "context_window", "max_output_tokens"], json.Select(p => p.Key));
        Assert.Equal("USD", (string?)json["currency"]);
        Assert.Equal(document, roundTripped);
    }

    [Fact]
    public void FromDomain_MapsCurrency()
    {
        var entry = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[1];

        Assert.Equal(entry.Currency, ModelEntryDocument.FromDomain(entry).Currency);
    }

    [Fact]
    public void DeepReasoningMappingV1_OpusIsPricedAtUsdListPrice()
    {
        var opus = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];

        Assert.Equal("claude-opus-4-8", opus.ModelId);
        Assert.Equal("USD", opus.Currency);
        Assert.Equal(0.0050m, opus.InputCostPer1k);
        Assert.Equal(0.0250m, opus.OutputCostPer1k);
        Assert.Equal(0.0005m, opus.CacheReadCostPer1k);
    }

    [Fact]
    public void DeepReasoningMappingV1_FableIsPricedAtUsdListPrice()
    {
        var fable = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[1];

        Assert.Equal("claude-fable-5", fable.ModelId);
        Assert.Equal("USD", fable.Currency);
        Assert.Equal(0.0100m, fable.InputCostPer1k);
        Assert.Equal(0.0500m, fable.OutputCostPer1k);
        Assert.Equal(0.0010m, fable.CacheReadCostPer1k);
    }
}
