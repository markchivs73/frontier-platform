using System.Diagnostics.CodeAnalysis;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The registry's contracts, as the compiler reads them (ADR-E27 D4, D15). Consumer-owned: the
/// composition root implements it over the resource registry. Publish embeds what it returns, and
/// <c>data.schema-ref-resolves</c> checks every ref a definition names against it.
/// </summary>
public interface IContractSnapshotCatalog
{
    /// <summary>The contract registered under exactly <paramref name="contractRef"/>, or null when there is none.</summary>
    Task<RegisteredContract?> FindAsync(string contractRef, CancellationToken cancellationToken);
}

/// <summary>A registry contract and whether it is still usable by a new publish.</summary>
/// <param name="Snapshot">The pinned snapshot: schema, rules and hash.</param>
/// <param name="IsRetired">Whether the contract is retired (ADR-CD9). A retired contract keeps serving pinned runs but cannot be named by a new publish.</param>
[ExcludeFromCodeCoverage(Justification = "Plain data carrier.")]
public sealed record RegisteredContract(ContractSnapshot Snapshot, bool IsRetired);
