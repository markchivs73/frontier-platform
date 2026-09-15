namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IModelResolver"/> over <see cref="IRoleRegistry"/> and
/// <see cref="ICircuitBreakerQuery"/> (doc 08 §5): resolves a role's mapping — exactly the pinned
/// version under a <see cref="ResolutionRequest.Pin"/> (ADR-PA29), otherwise the ring rules of
/// <see cref="ServedMappingSelector"/> — and walks the fallback chain skipping entries whose
/// circuit breaker is open.
/// </summary>
internal sealed class ModelResolver(IRoleRegistry roleRegistry, ICircuitBreakerQuery circuitBreakerQuery) : IModelResolver
{
    private readonly ServedMappingSelector selector = new(roleRegistry);

    /// <inheritdoc />
    public async Task<ResolvedModel> ResolveAsync(ResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mapping = ChainShape.EnsureValid(await GetEffectiveMappingAsync(request, cancellationToken));
        var (entry, chainPosition) = WalkChain(mapping);

        return new ResolvedModel
        {
            RoleId = mapping.RoleId,
            MappingVersion = mapping.MappingVersion,
            Provider = entry.Provider,
            ModelId = entry.TargetId,
            ModelVersion = null,
            ChainPosition = chainPosition,
            Entry = entry,
        };
    }

    /// <summary>
    /// Returns the mapping to serve: the pinned version as-is when <see cref="ResolutionRequest.Pin"/>
    /// is set (rings were decided at pin time), otherwise <see cref="ServedMappingSelector.SelectAsync"/>.
    /// </summary>
    internal Task<RoleMapping> GetEffectiveMappingAsync(ResolutionRequest request, CancellationToken ct) =>
        request.Pin is { } pin
            ? roleRegistry.GetMappingVersionAsync(request.RoleId, pin.MappingVersion, ct)
            : selector.SelectAsync(request.RoleId, request.EngagementId, request.MappingVersion, ct);

    /// <summary>
    /// Walks <see cref="RoleMapping.Chain"/> and returns the first healthy entry and its
    /// position. Skips entries whose circuit is open per <see cref="ICircuitBreakerQuery"/>.
    /// All-open → <see cref="InvalidOperationException"/> (whole-chain-down is sev-1, doc 08 §9).
    /// </summary>
    internal (ChainEntry entry, int chainPosition) WalkChain(RoleMapping mapping)
    {
        for (var i = 0; i < mapping.Chain.Count; i++)
        {
            var entry = mapping.Chain[i];
            if (!circuitBreakerQuery.IsOpen(entry.Provider, entry.TargetId))
                return (entry, i);
        }

        throw new InvalidOperationException(
            $"All models in chain for role '{mapping.RoleId}' v{mapping.MappingVersion} have open circuits (sev-1 event, doc 08 §9).");
    }
}
