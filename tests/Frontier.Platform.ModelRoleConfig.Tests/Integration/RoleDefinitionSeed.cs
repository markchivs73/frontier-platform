using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig.Tests.Integration;

/// <summary>
/// Seeds a role's current definition straight into the emulator, as the consumer's migration seed
/// does (ADR-PA40), so a mapping-governance test has the definition a proposal is now checked against.
/// </summary>
internal static class RoleDefinitionSeed
{
    /// <summary>Upserts <paramref name="roleId"/> as a model-backed v1 definition and points <c>current</c> at it; idempotent.</summary>
    internal static Task DefineModelRoleAsync(Container container, string roleId) =>
        DefineAsync(container, RoleDefinitionSamples.Migrated(RoleDefinitionSamples.Phase1Role(MappingProposalSamples.RoleId)) with { RoleId = roleId });

    /// <summary>Upserts <paramref name="definition"/> and points its role's <c>current</c> at it; idempotent.</summary>
    internal static async Task DefineAsync(Container container, RoleDefinition definition)
    {
        var document = RoleDefinitionDocument.FromDomain(definition);
        await container.UpsertItemAsync(document, new PartitionKey(definition.RoleId));
        await container.UpsertItemAsync(RoleDefinitionCurrentDocument.For(document), new PartitionKey(definition.RoleId));
    }
}
