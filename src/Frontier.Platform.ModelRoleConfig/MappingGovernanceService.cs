namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// <see cref="IMappingGovernanceService"/> (doc 08 §7-8): propose/approve are stubs until
/// the governance proposal store is built (Stage 9+). <see cref="RollbackAsync"/> is fully
/// implemented (ADR-M3): instant pointer rewrite via <see cref="IRoleMappingWriter"/>,
/// no approval required when degradation is confirmed.
/// </summary>
internal sealed class MappingGovernanceService(IRoleMappingWriter writer) : IMappingGovernanceService
{
    /// <inheritdoc />
    public Task<MappingChangeProposal> ProposeChangeAsync(MappingChange change, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Mapping change governance (doc 08 §7) requires a proposal store — deferred to Stage 9+.");

    /// <inheritdoc />
    public Task ApproveAsync(string proposalId, string approverId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Mapping change approval (doc 08 §7) requires a proposal store — deferred to Stage 9+.");

    /// <inheritdoc />
    public async Task RollbackAsync(string roleId, int toVersion, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleId);
        ArgumentNullException.ThrowIfNull(reason);

        await writer.WriteCurrentAsync(roleId, toVersion, cancellationToken);
    }
}
