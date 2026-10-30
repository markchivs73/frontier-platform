using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// The registry's contracts, as the platform reads them (ADR-E27 D4, D15). Consumer-owned: the
/// composition root implements it over the resource registry, once. The compiler embeds what it
/// returns at publish and checks every ref a definition names against it, and the chat designer
/// shows the design agent what each one says (ADR-PA41); ModelRoleConfig resolves a role
/// definition's contract refs through it and stamps their hashes at approval.
/// <para>
/// Kernel vocabulary since v0.45.0 (ADR-PA40): it moved here from
/// <c>Frontier.Platform.Workflow.Compiler</c>, names and members unchanged, so that a library below
/// the compiler can resolve a ref through the same port rather than a second one over the same store.
/// </para>
/// </summary>
public interface IContractSnapshotCatalog
{
    /// <summary>The contract registered under exactly <paramref name="contractRef"/>, or null when there is none.</summary>
    Task<RegisteredContract?> FindAsync(string contractRef, CancellationToken cancellationToken);

    /// <summary>
    /// Every registered contract that is not retired, in any order (ADR-PA41). The design agent is
    /// shown each one's annotations; a retired contract cannot be named by a new publish, so it is
    /// never offered.
    /// </summary>
    Task<IReadOnlyList<ContractSnapshot>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>A registry contract and whether it is still usable by a new publish.</summary>
/// <param name="Snapshot">The pinned snapshot: schema, rules and hash.</param>
/// <param name="IsRetired">Whether the contract is retired (ADR-CD9). A retired contract keeps serving pinned runs but cannot be named by a new publish.</param>
[ExcludeFromCodeCoverage(Justification = "Plain data carrier.")]
public sealed record RegisteredContract(ContractSnapshot Snapshot, bool IsRetired);
