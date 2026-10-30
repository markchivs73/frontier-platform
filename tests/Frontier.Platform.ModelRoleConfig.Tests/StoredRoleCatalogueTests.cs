using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="StoredRoleCatalogue"/>: the catalogue is the stored current definitions.</summary>
public sealed class StoredRoleCatalogueTests
{
    [Fact]
    public void Assemble_NothingStored_IsAnEmptyCatalogue_NotTheConstant()
    {
        var catalogue = StoredRoleCatalogue.Assemble([], []);

        Assert.Empty(catalogue.Roles);
    }

    [Fact]
    public void Assemble_ReturnsEachPointersVersion_OrderedByRoleId()
    {
        var versions = Phase1.Roles.Select(role => RoleDefinitionDocument.FromDomain(Migrated(role))).ToList();
        var pointers = versions.Select(RoleDefinitionCurrentDocument.For).ToList();

        var catalogue = StoredRoleCatalogue.Assemble(pointers, versions);

        Assert.Equal(["deep-reasoning", "embeddings", "fast", "structured-extraction"], catalogue.Roles.Select(role => role.RoleId));
        Assert.Equal(Migrated(Phase1Role("fast")), catalogue.Roles.Single(role => role.RoleId == "fast"));
    }

    [Fact]
    public void Assemble_ServesThePointedVersion_NotTheNewest()
    {
        var v1 = RoleDefinitionDocument.FromDomain(Migrated(Phase1Role("fast")));
        var v2 = RoleDefinitionDocument.FromDomain(Migrated(Phase1Role("fast")) with { DefinitionVersion = 2, Description = "v2" });

        var catalogue = StoredRoleCatalogue.Assemble([RoleDefinitionCurrentDocument.For(v1)], [v2, v1]);

        Assert.Equal(1, Assert.Single(catalogue.Roles).DefinitionVersion);
    }

    [Fact]
    public void Assemble_PointerToAMissingVersion_IsRefusedPermanently()
    {
        var v1 = RoleDefinitionDocument.FromDomain(Migrated(Phase1Role("fast")));

        var ex = Assert.Throws<ContractViolationException>(() => StoredRoleCatalogue.Assemble([RoleDefinitionCurrentDocument.For(v1)], []));

        Assert.Contains("fast:definition:v1", Assert.Single(ex.Violations), StringComparison.Ordinal);
    }
}
