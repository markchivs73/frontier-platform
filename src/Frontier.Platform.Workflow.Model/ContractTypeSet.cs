using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// The set of <see cref="IVersionedContract"/> types a deployment's workload declares,
/// supplied by the composition root (E16 option 2, decided at S13.12c).
///
/// Before this, three engine components each reflected over
/// <c>typeof(WorkflowDefinition).Assembly</c> independently — which hard-anchors the engine
/// on a *workload* assembly and is exactly the coupling E3b's interpreter move must break
/// (an engine package cannot know where its consumer's contracts live). Registration is now
/// an explicit, reviewable act at composition; the engine only ever consumes this port.
///
/// Reflection is not banned — it is *relocated* to the composition root, where knowing the
/// workload assembly is legitimate (see the Host's <c>WorkloadContractTypes</c>).
/// </summary>
public interface IContractTypeSet
{
    /// <summary>Contract type names, ordinally sorted (the <c>nameof(X)</c> convention callers use, e.g. <c>"InvoiceSummary"</c>).</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Resolves <paramref name="contractTypeName"/> to its CLR type, or <see langword="null"/> when the workload declares no such contract.</summary>
    Type? Resolve(string contractTypeName);
}

/// <summary>
/// Immutable <see cref="IContractTypeSet"/> over an explicit type list. Rejects a type that
/// does not implement <see cref="IVersionedContract"/> at construction, so a mis-registration
/// fails at composition (boot) rather than at first use (doc 12 §6's options-with-teeth posture).
/// </summary>
public sealed class ContractTypeSet : IContractTypeSet
{
    private readonly IReadOnlyDictionary<string, Type> typesByName;

    /// <summary>Builds the set from <paramref name="contractTypes"/>, validating each implements <see cref="IVersionedContract"/>.</summary>
    public ContractTypeSet(IEnumerable<Type> contractTypes)
    {
        ArgumentNullException.ThrowIfNull(contractTypes);

        var types = contractTypes.ToList();
        var invalid = types.Where(t => !typeof(IVersionedContract).IsAssignableFrom(t) || t.IsAbstract || !t.IsClass).ToList();
        if (invalid.Count > 0)
        {
            throw new ArgumentException(
                $"Every registered contract type must be a concrete class implementing {nameof(IVersionedContract)}; offending: {string.Join(", ", invalid.Select(t => t.Name))}.",
                nameof(contractTypes));
        }

        typesByName = types.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
        Names = [.. typesByName.Keys.OrderBy(name => name, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc />
    public Type? Resolve(string contractTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractTypeName);

        return typesByName.GetValueOrDefault(contractTypeName);
    }
}
