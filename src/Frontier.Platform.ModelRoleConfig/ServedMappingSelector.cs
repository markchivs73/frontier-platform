using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The ring rules of doc 08 §5, in one place: which mapping version an engagement is
/// <b>served</b> for a role. Shared by <see cref="ModelResolver"/> (unpinned resolution) and
/// <see cref="MappingPinner"/> (the pin at execution start, ADR-PA29), so the two cannot drift.
/// </summary>
internal sealed class ServedMappingSelector(IRoleRegistry roleRegistry)
{
    /// <summary>
    /// Reads <paramref name="version"/> (or the active mapping when null) and applies the ring
    /// rules: shadow → fleet predecessor; canary → engagement-stable hash check → fleet
    /// predecessor if not assigned.
    /// </summary>
    internal async Task<RoleMapping> SelectAsync(string roleId, string engagementId, int? version, CancellationToken ct)
    {
        var mapping = version is { } pinned
            ? await roleRegistry.GetMappingVersionAsync(roleId, pinned, ct)
            : await roleRegistry.GetActiveMappingAsync(roleId, ct);

        if (mapping.Ring == RolloutRing.Shadow)
            return await GetFleetFallbackAsync(roleId, mapping, ct);

        if (mapping.Ring == RolloutRing.Canary && !IsInCanary(engagementId, mapping.CanaryPercent))
            return await GetFleetFallbackAsync(roleId, mapping, ct);

        return mapping;
    }

    /// <summary>
    /// Deterministic engagement-stable canary assignment (doc 08 §5): SHA-256 hash of
    /// <paramref name="engagementId"/>, first 4 bytes as a big-endian uint32, modulo 100.
    /// An engagement is always wholly in or out of a canary ring.
    /// </summary>
    internal static bool IsInCanary(string engagementId, int canaryPercent)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(engagementId));
        var bucket = (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % 100);
        return bucket < canaryPercent;
    }

    /// <summary>Reads the fleet predecessor of a shadow or unassigned canary mapping; throws when none is recorded.</summary>
    internal async Task<RoleMapping> GetFleetFallbackAsync(string roleId, RoleMapping mapping, CancellationToken ct)
    {
        if (mapping.PredecessorFleetVersion is not { } fleetVersion)
            throw new InvalidOperationException(
                $"Role '{roleId}' mapping v{mapping.MappingVersion} has ring '{mapping.Ring.Name}' but PredecessorFleetVersion is not set.");

        return await roleRegistry.GetMappingVersionAsync(roleId, fleetVersion, ct);
    }
}
