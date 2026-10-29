using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// How a contract reached the registry (ADR-E27 D9). The registry copy is always the contract;
/// provenance decides only how strictly the legibility lint applies (ADR-E27 D10).
/// </summary>
public sealed class ContractProvenance : SmartEnum<ContractProvenance>
{
    /// <summary>Written in the registry by hand: A2A and Foundry agents.</summary>
    public static readonly ContractProvenance Authored = new("authored", lintRefuses: true);

    /// <summary>Generated from a .NET type with <c>JsonSchemaExporter</c>, then registered.</summary>
    public static readonly ContractProvenance Generated = new("generated", lintRefuses: true);

    /// <summary>Snapshotted from an MCP server's <c>tools/list</c>. Never registered by hand.</summary>
    public static readonly ContractProvenance Discovered = new("discovered", lintRefuses: false);

    private ContractProvenance(string name, bool lintRefuses)
        : base(name)
    {
        LintRefuses = lintRefuses;
    }

    /// <summary>
    /// Whether a legibility finding refuses registration. A discovered contract is someone else's,
    /// so its legibility findings are warnings (ADR-E27 D10).
    /// </summary>
    public bool LintRefuses { get; }
}
