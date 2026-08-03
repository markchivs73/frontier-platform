namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3/C-3 tests for the frozen Phase 1 role catalogue and <c>deep-reasoning</c> mapping (doc 08 §4, §6).</summary>
public sealed class Phase1RoleCatalogueTests
{
    [Fact]
    public void Catalogue_ContainsExactlyTheFourPhase1Roles()
    {
        var roleIds = Phase1RoleCatalogue.Catalogue.Roles.Select(role => role.RoleId);

        Assert.Equal(["deep-reasoning", "fast", "structured-extraction", "embeddings"], roleIds);
    }

    [Fact]
    public void Catalogue_DeepReasoning_IsMaterialStakes()
    {
        var deepReasoning = Phase1RoleCatalogue.Catalogue.Roles.Single(role => role.RoleId == "deep-reasoning");

        Assert.Equal(StakesLevel.Material, deepReasoning.Stakes);
    }

    [Fact]
    public void Catalogue_Embeddings_IsMechanicalStakes()
    {
        var embeddings = Phase1RoleCatalogue.Catalogue.Roles.Single(role => role.RoleId == "embeddings");

        Assert.Equal(StakesLevel.Mechanical, embeddings.Stakes);
    }

    [Fact]
    public void DeepReasoningMappingV1_ChainIsOpusThenFableOnAnthropic()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1;

        Assert.Equal("deep-reasoning", mapping.RoleId);
        Assert.Equal(1, mapping.MappingVersion);
        Assert.Equal(RolloutRing.Fleet, mapping.Ring);
        Assert.Collection(
            mapping.Chain,
            primary =>
            {
                Assert.Equal("anthropic", primary.Provider);
                Assert.Equal("claude-opus-4-8", primary.ModelId);
            },
            fallback =>
            {
                Assert.Equal("anthropic", fallback.Provider);
                Assert.Equal("claude-fable-5", fallback.ModelId);
            });
    }

    [Fact]
    public void DeepReasoningMappingV1_ApprovedByMark()
    {
        Assert.Equal("user:mark", Phase1RoleCatalogue.DeepReasoningMappingV1.ApprovedBy);
    }
}
