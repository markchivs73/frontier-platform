namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>Role definitions for the ADR-PA40 tests: the Phase 1 migration source, and stamped and agent-backed variants.</summary>
internal static class RoleDefinitionSamples
{
    /// <summary>The principal the consumer's seed attributes the migration to.</summary>
    internal const string MigrationPrincipal = "system:role-definition-migration";

    /// <summary>When the sample migration takes effect.</summary>
    internal static readonly DateTime MigratedAtUtc = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

#pragma warning disable CS0618 // The migration source is what these tests compare against (ADR-PA40).
    /// <summary>The four Phase 1 roles, unversioned.</summary>
    internal static RoleCatalogue Phase1 => Phase1RoleCatalogue.Catalogue;
#pragma warning restore CS0618

    /// <summary>The Phase 1 role <paramref name="roleId"/>, unversioned.</summary>
    internal static RoleDefinition Phase1Role(string roleId) => Phase1.Roles.Single(role => role.RoleId == roleId);

    /// <summary><paramref name="definition"/> stamped as the migration's v1.</summary>
    internal static RoleDefinition Migrated(RoleDefinition definition) => definition with
    {
        DefinitionVersion = 1,
        ApprovedBy = MigrationPrincipal,
        EffectiveFromUtc = MigratedAtUtc,
    };

    /// <summary>An approved agent-backed role with both contracts and no requirements.</summary>
    internal static RoleDefinition AgentBacked(string roleId = "gen-record-pass") => new()
    {
        RoleId = roleId,
        Description = "Genealogy record pass, served by a registered agent.",
        Stakes = StakesLevel.Standard,
        Backing = RoleBacking.Agent,
        InputContractRef = "genealogy/record-pass-input/1.0",
        InputContractHash = "sha256:" + new string('a', 64),
        OutputContractRef = "genealogy/record-pass-output/1.0",
        OutputContractHash = "sha256:" + new string('b', 64),
        DefinitionVersion = 2,
        ApprovedBy = "user:oid-approver",
        EffectiveFromUtc = MigratedAtUtc,
    };
}
