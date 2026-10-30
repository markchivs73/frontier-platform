using System.Text;
using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.projection-compatible (doc 13 §4.2; ADR-E27 D14): a data edge's projection builds an input
/// the consumer's contract accepts. Both ends must be registry contracts, since a CLR type has no
/// schema to project from. Each target is a declared input property; a pointer's value has exactly
/// the shape of that property, annotations aside (the strict reading of ADR-E27 D5, no subtyping);
/// a literal satisfies it; and every required input property is projected from a value that is
/// always present, or from a literal.
/// </summary>
public sealed class DataProjectionCompatibleRule : IDefinitionValidationRule
{
    private readonly IContractSnapshotCatalog _contracts;
    private readonly IContractValidator _validator;

    /// <summary>Constructs the rule over the registry's contracts and the contract validator.</summary>
    public DataProjectionCompatibleRule(IContractSnapshotCatalog contracts, IContractValidator validator)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(validator);
        _contracts = contracts;
        _validator = validator;
    }

    public string RuleId => "data.projection-compatible";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var nodes = ctx.Definition.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();
        foreach (var edge in ctx.Definition.Edges.Where(e => e.Kind == EdgeKind.Data && e.Projection is not null))
        {
            var source = await SchemaAsync(OutputRef(nodes.GetValueOrDefault(edge.FromNodeId)), ct);
            var target = await SchemaAsync(InputRef(nodes.GetValueOrDefault(edge.ToNodeId)), ct);
            findings.AddRange(source is { } s && target is { } t
                ? EdgeFindings(edge, s, t).Select(m => Finding(edge, m))
                : [Finding(edge, "a projection needs registered contracts at both ends: the producer's output and the consumer's input.")]);
        }

        return findings;
    }

    /// <summary>Every way <paramref name="edge"/>'s projection fails to fit <paramref name="target"/> from <paramref name="source"/>.</summary>
    internal IEnumerable<string> EdgeFindings(WorkflowEdge edge, JsonElement source, JsonElement target)
    {
        foreach (var field in edge.Projection!)
        {
            if (FieldFinding(field, source, target) is { } finding)
            {
                yield return finding;
            }
        }

        var projected = edge.Projection!.Select(f => f.Target).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in ContractShapes.RequiredProperties(target).Where(p => !projected.Contains(p)))
        {
            yield return $"the consumer requires '{missing}', which the projection does not set.";
        }
    }

    /// <summary>Why one projected field does not fit, or null when it does.</summary>
    internal string? FieldFinding(ProjectionField field, JsonElement source, JsonElement target)
    {
        var pointer = "/" + Escape(field.Target);
        if (ContractShapes.ValueSchema(target, pointer) is not { } wanted)
        {
            return $"'{field.Target}' is not a property of the consumer's input.";
        }

        return field.Literal is { } literal
            ? LiteralFinding(field.Target, literal, wanted)
            : PointerFinding(field, source, wanted, ContractShapes.RequiredProperties(target).Contains(field.Target));
    }

    /// <summary>Why a pointer's value does not fit the target property, or null.</summary>
    internal static string? PointerFinding(ProjectionField field, JsonElement source, JsonElement wanted, bool required)
    {
        if (ContractShapes.ValueSchema(source, field.From!) is not { } found)
        {
            return $"'{field.Target}' reads '{field.From}', which the producer's output does not declare.";
        }

        if (!ContractShapes.SameShape(found, wanted))
        {
            return $"'{field.Target}' reads '{field.From}', whose shape differs from the consumer's '{field.Target}'.";
        }

        return required && !ContractShapes.AlwaysPresent(source, field.From!)
            ? $"'{field.Target}' is required, but '{field.From}' is not always present in the producer's output."
            : null;
    }

    /// <summary>Why a literal does not satisfy the target property, or null.</summary>
    internal string? LiteralFinding(string targetName, JsonElement literal, JsonElement wanted)
    {
        var snapshot = new ContractSnapshot { ContractRef = "frontier/projection-literal/1.0", Schema = wanted, Rules = [], Hash = ContractHash.Compute(wanted, []) };
        return _validator.Validate(snapshot, Encoding.UTF8.GetBytes(literal.GetRawText())).IsValid
            ? null
            : $"the literal for '{targetName}' does not satisfy the consumer's '{targetName}'.";
    }

    /// <summary>The registered schema for <paramref name="contractRef"/>, or null when there is none to project with.</summary>
    internal async Task<JsonElement?> SchemaAsync(string? contractRef, CancellationToken ct)
    {
        if (!DataSchemaRefMatchRule.IsSchemaRef(contractRef))
        {
            return null;
        }

        return EngineContracts.Find(contractRef!) is { } engineOwned
            ? engineOwned.Schema
            : (await _contracts.FindAsync(contractRef!, ct))?.Snapshot.Schema;
    }

    /// <summary>The contract a node produces: its output contract, or for a gate the gate decision.</summary>
    internal static string? OutputRef(WorkflowNode? node) => node switch
    {
        AgentTaskNode agent => agent.OutputContractType,
        McpToolNode tool => tool.OutputContractType,
        HumanGateNode => GateDecision.ContractRef,
        _ => null,
    };

    /// <summary>The contract a node takes as input.</summary>
    internal static string? InputRef(WorkflowNode? node) => node switch
    {
        AgentTaskNode agent => agent.InputContractType,
        McpToolNode tool => tool.InputContractType,
        _ => null,
    };

    private ValidationFinding Finding(WorkflowEdge edge, string message) =>
        new(RuleId, DefaultSeverity, message, edge.ToNodeId, EdgeRef: $"{edge.FromNodeId}->{edge.ToNodeId}", FieldPath: "projection");

    private static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
