namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Deterministic Cosmos document <c>id</c> formatting for the <c>model-role-config</c>
/// container (doc 08 §6): <c>{roleId}:v{mappingVersion}</c> for append-only mapping
/// versions, <c>{roleId}:current</c> for the mutable pointer. Pure and side-effect free
/// so <see cref="CosmosRoleRegistry"/> can be unit-tested without the Cosmos SDK.
/// </summary>
internal static class ModelRoleConfigDocumentId
{
    /// <summary>Builds the mapping-version document id for <paramref name="roleId"/> at <paramref name="mappingVersion"/>.</summary>
    internal static string ForVersion(string roleId, int mappingVersion) =>
        $"{roleId}:v{mappingVersion}";

    /// <summary>Builds the <c>current</c> pointer document id for <paramref name="roleId"/>.</summary>
    internal static string ForCurrent(string roleId) =>
        $"{roleId}:current";
}
