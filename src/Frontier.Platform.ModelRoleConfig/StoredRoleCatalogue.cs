using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Builds the <see cref="RoleCatalogue"/> from stored definitions (ADR-PA40): each role's
/// <c>{roleId}:definition:current</c> pointer joined to the version document it names, ordered by
/// role id so the catalogue is the same on every read. Pure, so <see cref="CosmosRoleRegistry"/>'s
/// two queries stay the only untested glue.
/// </summary>
internal static class StoredRoleCatalogue
{
    /// <summary>
    /// Joins <paramref name="pointers"/> to <paramref name="versions"/> by id. A pointer naming a
    /// version that is not stored is corrupt governance data, refused permanently rather than
    /// dropped: a silently missing role would surface later as an unrelated publish finding.
    /// </summary>
    internal static RoleCatalogue Assemble(IEnumerable<RoleDefinitionCurrentDocument> pointers, IEnumerable<RoleDefinitionDocument> versions)
    {
        var byId = versions.ToDictionary(version => version.Id, StringComparer.Ordinal);
        var roles = pointers
            .OrderBy(pointer => pointer.RoleId, StringComparer.Ordinal)
            .Select(pointer => byId.TryGetValue(pointer.CurrentRef, out var version)
                ? version.ToDomain()
                : throw new ContractViolationException(nameof(RoleDefinition), [$"role '{pointer.RoleId}' points at definition '{pointer.CurrentRef}', which is not stored (ADR-PA40)."]))
            .ToArray();

        return new RoleCatalogue { Roles = roles };
    }
}
