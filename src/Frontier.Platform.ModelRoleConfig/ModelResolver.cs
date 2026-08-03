using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IModelResolver"/> over <see cref="IRoleRegistry"/> and
/// <see cref="ICircuitBreakerQuery"/> (doc 08 §5): resolves a role's pinned (or active)
/// mapping, applies ring rules (shadow = never serve; canary = engagement-stable % check),
/// and walks the fallback chain skipping entries whose circuit breaker is open.
/// </summary>
internal sealed class ModelResolver(IRoleRegistry roleRegistry, ICircuitBreakerQuery circuitBreakerQuery) : IModelResolver
{
    /// <inheritdoc />
    public async Task<ResolvedModel> ResolveAsync(ResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mapping = await GetEffectiveMappingAsync(request, cancellationToken);
        var (entry, chainPosition) = WalkChain(mapping);

        return new ResolvedModel
        {
            RoleId = mapping.RoleId,
            MappingVersion = mapping.MappingVersion,
            Provider = entry.Provider,
            ModelId = entry.ModelId,
            ModelVersion = null,
            ChainPosition = chainPosition,
            Entry = entry,
        };
    }

    /// <summary>
    /// Returns the mapping to serve, applying ring rules: shadow → fleet fallback;
    /// canary → engagement-stable hash check → fleet fallback if not assigned.
    /// </summary>
    internal async Task<RoleMapping> GetEffectiveMappingAsync(ResolutionRequest request, CancellationToken ct)
    {
        var mapping = request.MappingVersion is { } pinned
            ? await roleRegistry.GetMappingVersionAsync(request.RoleId, pinned, ct)
            : await roleRegistry.GetActiveMappingAsync(request.RoleId, ct);

        if (mapping.Ring == RolloutRing.Shadow)
            return await GetFleetFallbackAsync(request.RoleId, mapping, ct);

        if (mapping.Ring == RolloutRing.Canary && !IsInCanary(request.EngagementId, mapping.CanaryPercent))
            return await GetFleetFallbackAsync(request.RoleId, mapping, ct);

        return mapping;
    }

    /// <summary>
    /// Walks <see cref="RoleMapping.Chain"/> and returns the first healthy entry and its
    /// position. Skips entries whose circuit is open per <see cref="ICircuitBreakerQuery"/>.
    /// All-open → <see cref="InvalidOperationException"/> (whole-chain-down is sev-1, doc 08 §9).
    /// </summary>
    internal (ModelEntry entry, int chainPosition) WalkChain(RoleMapping mapping)
    {
        for (var i = 0; i < mapping.Chain.Count; i++)
        {
            var entry = mapping.Chain[i];
            if (!circuitBreakerQuery.IsOpen(entry.Provider, entry.ModelId))
                return (entry, i);
        }

        throw new InvalidOperationException(
            $"All models in chain for role '{mapping.RoleId}' v{mapping.MappingVersion} have open circuits (sev-1 event, doc 08 §9).");
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

    private async Task<RoleMapping> GetFleetFallbackAsync(string roleId, RoleMapping mapping, CancellationToken ct)
    {
        if (mapping.PredecessorFleetVersion is not { } fleetVersion)
            throw new InvalidOperationException(
                $"Role '{roleId}' mapping v{mapping.MappingVersion} has ring '{mapping.Ring.Name}' but PredecessorFleetVersion is not set.");

        return await roleRegistry.GetMappingVersionAsync(roleId, fleetVersion, ct);
    }
}
