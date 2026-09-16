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

    /// <summary>
    /// The id prefix every mapping-version document of <paramref name="roleId"/> shares, and which
    /// neither the <c>current</c> pointer nor a proposal matches — the positive discriminator the
    /// version listing selects on.
    /// </summary>
    internal static string VersionPrefix(string roleId) => $"{roleId}:v";

    /// <summary>
    /// Builds the proposal document id (ADR-PA32): <c>{roleId}:proposal:{proposalId}</c>. The
    /// <c>:proposal:</c> infix cannot collide with <c>:v{n}</c> or <c>:current</c>, so all three kinds
    /// share the role's partition without ambiguity.
    /// </summary>
    internal static string ForProposal(string roleId, string proposalId) =>
        $"{roleId}:proposal:{proposalId}";
}
