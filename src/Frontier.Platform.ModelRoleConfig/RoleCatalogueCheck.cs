using System.Net;
using Frontier.Platform.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Boot check (doc 12 §6, doc 08 §6): every role id referenced by a published workflow
/// definition (supplied by the consumer-owned <see cref="IReferencedRolesSource"/>, ADR-PA2)
/// must have a current stored definition in <see cref="IRoleRegistry.GetCatalogueAsync"/> (ADR-PA40,
/// the only way a missed role migration surfaces — there is no fallback to a code constant) and an
/// active mapping (<see cref="IRoleRegistry.GetActiveMappingAsync"/>) whose
/// <see cref="RoleMapping.Ring"/> is <see cref="RolloutRing.Fleet"/> or
/// <see cref="RolloutRing.Canary"/> — catching a role left orphaned by a catalogue or
/// mapping edit while a published definition still references it.
/// </summary>
internal sealed class RoleCatalogueCheck(IReferencedRolesSource rolesSource, IRoleRegistry roleRegistry) : IStartupCheck
{
    /// <inheritdoc />
    public string Name => "RoleCatalogue";

    /// <inheritdoc />
    public async Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var referencedRoles = await rolesSource.GetReferencedRoleIdsAsync(cancellationToken);

        return await EvaluateAsync(referencedRoles, roleRegistry, cancellationToken);
    }

    /// <summary>
    /// Confirms every role in <paramref name="referencedRoles"/> has a current definition and
    /// resolves to an active, fleet/canary mapping via <paramref name="roleRegistry"/>. A missing <c>current</c>
    /// pointer document surfaces as <see cref="CosmosException"/> 404 from
    /// <see cref="CosmosRoleRegistry"/> — caught here rather than added to
    /// <see cref="IRoleRegistry"/>'s contract, since both types are internal to this
    /// library (a <c>TryGetActiveMappingAsync</c> can be added if a future consumer needs it).
    /// </summary>
    internal static async Task<StartupCheckResult> EvaluateAsync(IReadOnlySet<string> referencedRoles, IRoleRegistry roleRegistry, CancellationToken cancellationToken)
    {
        var catalogue = await roleRegistry.GetCatalogueAsync(cancellationToken);
        var defined = catalogue.Roles.Select(role => role.RoleId).ToHashSet(StringComparer.Ordinal);

        foreach (var roleId in referencedRoles)
        {
            if (!defined.Contains(roleId))
            {
                return StartupCheckResult.Fail($"Role '{roleId}' is referenced by a published workflow definition but has no current role definition; seed or approve one (ADR-PA40).");
            }

            RoleMapping mapping;

            try
            {
                mapping = await roleRegistry.GetActiveMappingAsync(roleId, cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return StartupCheckResult.Fail($"Role '{roleId}' is referenced by a published workflow definition but has no active mapping (doc 08 §6, doc 12 §6).");
            }

            if (mapping.Ring != RolloutRing.Fleet && mapping.Ring != RolloutRing.Canary)
            {
                return StartupCheckResult.Fail($"Role '{roleId}' is referenced by a published workflow definition but its active mapping is ring '{mapping.Ring}', not fleet or canary (doc 08 §6, doc 12 §6).");
            }
        }

        return StartupCheckResult.Pass();
    }
}
