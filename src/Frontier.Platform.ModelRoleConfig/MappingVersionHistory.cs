namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IMappingVersionHistory"/> over the proposal store (ADR-PA34). One query for the
/// role's version documents plus one point-read of its <c>current</c> pointer — not a point-read
/// per version, which would cost one request per row of a D3 history panel.
/// </summary>
internal sealed class MappingVersionHistory(IMappingProposalStore store) : IMappingVersionHistory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MappingVersionSummary>> GetVersionHistoryAsync(string roleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        var mappings = await store.ListMappingsAsync(roleId, cancellationToken);
        var current = await store.FindCurrentVersionAsync(roleId, cancellationToken);

        return [.. mappings.OrderBy(mapping => mapping.MappingVersion).Select(mapping => Summarise(mapping, current))];
    }

    /// <summary>Projects a stored version onto its history row, marking the one <c>current</c> names.</summary>
    internal static MappingVersionSummary Summarise(RoleMapping mapping, int? currentVersion) => new()
    {
        MappingVersion = mapping.MappingVersion,
        Ring = mapping.Ring,
        CanaryPercent = mapping.CanaryPercent,
        Chain = mapping.Chain,
        ChangeReason = mapping.ChangeReason,
        ApprovedBy = mapping.ApprovedBy,
        EffectiveFromUtc = mapping.EffectiveFromUtc,
        IsCurrent = currentVersion == mapping.MappingVersion,
    };
}
