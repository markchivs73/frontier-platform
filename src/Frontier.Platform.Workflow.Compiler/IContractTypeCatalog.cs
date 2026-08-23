using Frontier.Platform.Abstractions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Resolves wire contract type names (e.g. <c>"ScopeSection"</c>) for the <c>data.*</c>
/// validation rules (doc 13 §4.2 R2, S9.30). Mirrors the runtime's reflection-based registry
/// (Orchestration's <c>ContractTypeRegistry</c>, ADR-CD3): a definition may only reference
/// contract types the runtime can actually deserialize.
/// </summary>
public interface IContractTypeCatalog
{
    /// <summary>Whether <paramref name="contractTypeName"/> names a known versioned contract type.</summary>
    bool Resolves(string contractTypeName);

    /// <summary>Resolves <paramref name="contractTypeName"/> to its CLR type, or <see langword="null"/> if unknown (S9.43: backs the test-run "Expected shape" schema/example builder).</summary>
    Type? Resolve(string contractTypeName);

    /// <summary>
    /// Contract type names offered to designers, sorted ordinally (S9.76: backs the
    /// contract-catalogue discovery endpoint + the Inspector contract picker).
    ///
    /// S13.7h narrowed this to types that can actually be an agent's structured output: it
    /// previously offered every <c>IVersionedContract</c>, including internal projections and
    /// persistence entities, and the design agent duly picked one whose open map the model
    /// provider rejects at run time. <see cref="Resolves"/> stays broad — an existing definition
    /// must still resolve its names, and unusable *outputs* are reported by
    /// <c>data.output-contract-bindable</c> with a message that explains itself.
    /// </summary>
    IReadOnlyList<string> Names { get; }
}

/// <summary>
/// Default <see cref="IContractTypeCatalog"/>: every non-abstract class implementing
/// <see cref="IVersionedContract"/> in the Abstractions assembly, keyed by CLR name — the exact
/// name-matching the runtime registry uses, so design-time resolution can never diverge from it.
/// </summary>
public sealed class ReflectionContractTypeCatalog : IContractTypeCatalog
{
    // S13.12c (E16 option 2): the contract set arrives from the composition root rather than
    // being reflected here — an engine package cannot anchor on a workload assembly (ADR-E3a).
    private readonly IContractTypeSet contractTypes;

    /// <summary>Constructs the catalogue over the composition-supplied contract set.</summary>
    public ReflectionContractTypeCatalog(IContractTypeSet contractTypes)
    {
        ArgumentNullException.ThrowIfNull(contractTypes);
        this.contractTypes = contractTypes;
    }

    /// <inheritdoc />
    public bool Resolves(string contractTypeName) => contractTypes.Resolve(contractTypeName) is not null;

    /// <inheritdoc />
    public Type? Resolve(string contractTypeName) => contractTypes.Resolve(contractTypeName);

    // S13.7h: offer only what an agent can actually be asked to produce. Filtering here rather
    // than at each call site means the discovery endpoint, the Inspector picker and the designer
    // prompt all narrow together.
    /// <inheritdoc />
    public IReadOnlyList<string> Names =>
        [.. contractTypes.Names.Where(name => contractTypes.Resolve(name) is { } type && StrictSchemaCheck.IsBindable(type))];
}
