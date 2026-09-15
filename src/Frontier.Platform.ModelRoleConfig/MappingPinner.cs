using System.Net;
using Frontier.Platform.Abstractions;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IMappingPinner"/> over <see cref="IRoleRegistry"/> (ADR-PA29): each role's served
/// version, chosen by the same <see cref="ServedMappingSelector"/> the resolver uses.
/// </summary>
internal sealed class MappingPinner(IRoleRegistry roleRegistry) : IMappingPinner
{
    private readonly ServedMappingSelector selector = new(roleRegistry);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelRolePin>> PinAsync(string engagementId, IEnumerable<string> roleIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engagementId);
        ArgumentNullException.ThrowIfNull(roleIds);

        var pins = new List<ModelRolePin>();
        foreach (var roleId in roleIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            pins.Add(await PinRoleAsync(engagementId, roleId, cancellationToken));
        }

        return pins;
    }

    /// <summary>Pins one role, translating a missing mapping into a permanent contract violation naming the role.</summary>
    internal async Task<ModelRolePin> PinRoleAsync(string engagementId, string roleId, CancellationToken ct)
    {
        try
        {
            var served = await selector.SelectAsync(roleId, engagementId, version: null, ct);
            return new ModelRolePin { RoleId = roleId, MappingVersion = served.MappingVersion, Ring = served.Ring };
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ContractViolationException(nameof(RoleMapping), [$"Role '{roleId}' has no model-role mapping; the execution cannot be pinned (ADR-PA29)."]);
        }
    }
}
