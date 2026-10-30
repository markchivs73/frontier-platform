#pragma warning disable CS0618 // Phase1RoleCatalogue.Catalogue is obsolete as the catalogue but remains the migration source under test (ADR-PA40).

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
                Assert.Equal("claude-opus-4-8", Assert.IsType<ModelEntry>(primary).ModelId);
            },
            fallback =>
            {
                Assert.Equal("anthropic", fallback.Provider);
                Assert.Equal("claude-fable-5", Assert.IsType<ModelEntry>(fallback).ModelId);
            });
    }

    [Fact]
    public void DeepReasoningMappingV1_ApprovedByMark()
    {
        Assert.Equal("user:mark", Phase1RoleCatalogue.DeepReasoningMappingV1.ApprovedBy);
    }

    [Fact]
    public void Catalogue_EveryRoleIsModelBackedWithoutContractsOrStamps()
    {
        // ADR-PA40 migration: the four roles migrate as model-backed v1 definitions with no contracts.
        Assert.All(Phase1RoleCatalogue.Catalogue.Roles, role =>
        {
            Assert.Equal(RoleBacking.Model, role.Backing);
            Assert.NotNull(role.Requirements);
            Assert.Null(role.InputContractRef);
            Assert.Null(role.OutputContractRef);
            Assert.Null(role.DefinitionVersion);
            Assert.Null(role.ApprovedBy);
            Assert.Null(role.EffectiveFromUtc);
        });
    }

    [Fact]
    public void Catalogue_MigratedThroughTheStoredDocument_EqualsTheConstantFieldForField()
    {
        // The v1 documents the consumer seeds must equal this constant: storing and reading each role
        // back loses nothing but the approval stamps the migration adds.
        foreach (var role in Phase1RoleCatalogue.Catalogue.Roles)
        {
            var migrated = RoleDefinitionSamples.Migrated(role);

            var read = RoleDefinitionDocument.FromDomain(migrated).ToDomain();

            Assert.Equal(migrated, read);
            Assert.Equal(role, read with { DefinitionVersion = null, ApprovedBy = null, EffectiveFromUtc = null });
        }
    }
}
