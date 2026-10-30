using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Embeds every registry contract a definition names into the definition itself (ADR-E27 D15), so a
/// run validates against what was pinned at publish and never fetches a contract.
/// </summary>
public interface IDefinitionContractEmbedder
{
    /// <summary>
    /// <paramref name="definition"/> with <see cref="WorkflowDefinition.Contracts"/> set to the snapshot
    /// of every schema ref it names, sorted by ref; unchanged in shape when it names none.
    /// </summary>
    /// <exception cref="InvalidOperationException">A ref is not registered, or is retired.</exception>
    Task<WorkflowDefinition> EmbedAsync(WorkflowDefinition definition, CancellationToken cancellationToken);
}

/// <summary>
/// The schema refs a definition names (ADR-E2's <c>{namespace}/{name}/{major}.{minor}</c>; a CLR
/// contract name has no <c>/</c>): node input and output contracts and data-edge contract types,
/// distinct and ordinally sorted.
/// </summary>
public static class DefinitionContractRefs
{
    /// <summary>The schema refs <paramref name="definition"/> names.</summary>
    public static IReadOnlyList<string> Collect(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return [.. definition.Nodes.SelectMany(NodeRefs)
            .Concat(definition.Edges.Where(e => e.Kind == EdgeKind.Data).Select(e => e.ContractType))
            .Where(DataSchemaRefMatchRule.IsSchemaRef)
            .Select(r => r!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string?> NodeRefs(WorkflowNode node) => node switch
    {
        AgentTaskNode agent => [agent.InputContractType, agent.OutputContractType],
        McpToolNode tool => [tool.InputContractType, tool.OutputContractType],
        _ => [],
    };
}

/// <summary>
/// Default <see cref="IDefinitionContractEmbedder"/>. An engine-owned contract
/// (<see cref="EngineContracts"/>) is embedded from the engine; every other ref from the registry.
/// </summary>
internal sealed class DefinitionContractEmbedder(IContractSnapshotCatalog catalog) : IDefinitionContractEmbedder
{
    /// <inheritdoc />
    public async Task<WorkflowDefinition> EmbedAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var refs = DefinitionContractRefs.Collect(definition);
        if (refs.Count == 0)
        {
            return definition with { Contracts = null };
        }

        var snapshots = new List<ContractSnapshot>(refs.Count);
        var unusable = new List<string>();
        foreach (var contractRef in refs)
        {
            if (await ResolveAsync(contractRef, cancellationToken).ConfigureAwait(false) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
            else
            {
                unusable.Add(contractRef);
            }
        }

        return unusable.Count == 0
            ? definition with { Contracts = snapshots }
            : throw new InvalidOperationException(
                $"Cannot publish: these contracts are not registered, or are retired: {string.Join(", ", unusable)}. Register them, or name a current version.");
    }

    /// <summary>The snapshot to embed for <paramref name="contractRef"/>, or null when there is no usable one.</summary>
    internal async Task<ContractSnapshot?> ResolveAsync(string contractRef, CancellationToken cancellationToken)
    {
        if (EngineContracts.Find(contractRef) is { } engineOwned)
        {
            return engineOwned;
        }

        var registered = await catalog.FindAsync(contractRef, cancellationToken).ConfigureAwait(false);
        // A snapshot filed under another ref would pin the wrong contract, so it counts as missing.
        return registered is { IsRetired: false } && string.Equals(registered.Snapshot.ContractRef, contractRef, StringComparison.Ordinal)
            ? registered.Snapshot
            : null;
    }
}
