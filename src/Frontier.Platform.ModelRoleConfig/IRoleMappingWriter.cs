namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Write seam for the <c>model-role-config</c> current pointer (doc 08 §8 ADR-M3):
/// supports instant rollback by repointing <c>{roleId}:current</c> to a specific
/// historical version without rewriting the version document itself.
/// Implemented by <see cref="CosmosRoleRegistry"/>, which already holds the container
/// reference.
/// </summary>
internal interface IRoleMappingWriter
{
    /// <summary>Repoints the <c>current</c> pointer for <paramref name="roleId"/> to <paramref name="toVersion"/>.</summary>
    Task WriteCurrentAsync(string roleId, int toVersion, CancellationToken ct);
}
