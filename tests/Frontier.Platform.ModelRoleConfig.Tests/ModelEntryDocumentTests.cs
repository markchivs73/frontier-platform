namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="ModelEntryDocument"/>'s domain mapping (doc 08 §6).</summary>
public sealed class ModelEntryDocumentTests
{
    [Fact]
    public void FromDomain_ThenToDomain_RoundTrips()
    {
        var entry = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];

        var document = ModelEntryDocument.FromDomain(entry);
        var roundTripped = document.ToDomain();

        Assert.Equal(entry, roundTripped);
    }

    [Fact]
    public void FromDomain_MapsAllFields()
    {
        var entry = Phase1RoleCatalogue.DeepReasoningMappingV1.Chain[0];

        var document = ModelEntryDocument.FromDomain(entry);

        Assert.Equal(entry.Provider, document.Provider);
        Assert.Equal(entry.ModelId, document.ModelId);
        Assert.Equal(entry.InputCostPer1kGbp, document.InputCostPer1kGbp);
        Assert.Equal(entry.OutputCostPer1kGbp, document.OutputCostPer1kGbp);
        Assert.Equal(entry.CacheReadCostPer1kGbp, document.CacheReadCostPer1kGbp);
        Assert.Equal(entry.ContextWindow, document.ContextWindow);
        Assert.Equal(entry.MaxOutputTokens, document.MaxOutputTokens);
        Assert.Equal(entry.CachingStrategy, document.CachingStrategy);
    }
}
