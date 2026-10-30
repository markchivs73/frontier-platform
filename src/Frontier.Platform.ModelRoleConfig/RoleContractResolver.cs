using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Resolves a role definition's contract refs through the one contract port,
/// <see cref="IContractSnapshotCatalog"/> (ADR-PA40), and stamps their hashes. A ref that is not
/// registered, is retired, or comes back filed under another ref is refused — the last because it
/// would stamp the wrong contract's hash, as the compiler's embedder reasons.
/// </summary>
internal sealed class RoleContractResolver(IContractSnapshotCatalog catalog)
{
    /// <summary>
    /// <paramref name="definition"/> with the hash of each declared ref stamped from the registry.
    /// </summary>
    /// <exception cref="UnknownRoleContractException">A declared ref is unusable.</exception>
    internal async Task<RoleDefinition> StampAsync(RoleDefinition definition, CancellationToken cancellationToken)
    {
        var unusable = new List<string>();
        var input = await HashOfAsync(definition.InputContractRef, unusable, cancellationToken);
        var output = await HashOfAsync(definition.OutputContractRef, unusable, cancellationToken);

        return unusable.Count == 0
            ? definition with { InputContractHash = input, OutputContractHash = output }
            : throw new UnknownRoleContractException(nameof(RoleDefinition), unusable);
    }

    /// <summary>The registered hash of <paramref name="contractRef"/>; null when none is declared or it is unusable.</summary>
    internal async Task<string?> HashOfAsync(string? contractRef, List<string> unusable, CancellationToken cancellationToken)
    {
        if (contractRef is null)
        {
            return null;
        }

        var registered = await catalog.FindAsync(contractRef, cancellationToken);
        if (registered is { IsRetired: false } && string.Equals(registered.Snapshot.ContractRef, contractRef, StringComparison.Ordinal))
        {
            return registered.Snapshot.Hash;
        }

        unusable.Add($"contract '{contractRef}' is not registered, or is retired; register it, or name a current version (ADR-PA40).");
        return null;
    }
}
