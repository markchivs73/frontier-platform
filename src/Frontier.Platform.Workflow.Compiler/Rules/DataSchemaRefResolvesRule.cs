using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.schema-ref-resolves (doc 13 §4.2; ADR-E27 D15): every schema ref a node or data edge names
/// resolves to a registered contract that is not retired, or to an engine-owned contract. Without
/// it a well-formed but unknown ref passes validation and fails at publish, when the contracts are
/// pinned. Malformed refs are <c>data.schema-ref-match</c>'s.
/// </summary>
public sealed class DataSchemaRefResolvesRule : IDefinitionValidationRule
{
    private readonly IContractSnapshotCatalog _contracts;

    /// <summary>Constructs the rule over the registry's contracts.</summary>
    public DataSchemaRefResolvesRule(IContractSnapshotCatalog contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        _contracts = contracts;
    }

    public string RuleId => "data.schema-ref-resolves";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var findings = new List<ValidationFinding>();
        foreach (var contractRef in DefinitionContractRefs.Collect(ctx.Definition).Where(r => EngineContracts.Find(r) is null))
        {
            if (Problem(await _contracts.FindAsync(contractRef, ct)) is { } problem)
            {
                findings.Add(new ValidationFinding(RuleId, DefaultSeverity, $"contract '{contractRef}' {problem}", FieldPath: contractRef));
            }
        }

        return findings;
    }

    /// <summary>What is wrong with a lookup's result, or null when the contract is usable.</summary>
    internal static string? Problem(RegisteredContract? found) => found switch
    {
        null => "is not registered. Register it, or name a contract that is.",
        { IsRetired: true } => "is retired. Name a current version.",
        _ => null,
    };
}
