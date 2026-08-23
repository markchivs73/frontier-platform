using System.Text.RegularExpressions;

using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.schema-ref-match (doc 13 §4.2, S13.7d; ADR-E2 Decision 3): where a Data edge or
/// an <see cref="AgentTaskNode"/> contract field carries a capability-declared <em>schema
/// ref</em> (the ADR-E2 convention <c>{namespace}/{name}/{major}.{minor}</c> — distinguished
/// from CLR contract names by containing <c>/</c>), the ref must be well-formed and both
/// endpoints must agree on the <strong>exact schema id and major version</strong> — minors
/// may differ, majors never (<c>schemas/document-structure/1.x</c> matches <c>1.y</c>,
/// never <c>2.x</c>). No structural subtyping, no semver ranges (ADR-E2's frozen v1
/// semantics). Resolution of schema ids against the registry's capability-declared
/// schemas activates when pinned server cards carry schema ids — until then this rule
/// enforces format and match semantics, and CLR-named edges stay governed by
/// <c>data.edge-type-match</c>/<c>data.contract-types-resolve</c> untouched.
/// </summary>
public sealed partial class DataSchemaRefMatchRule : PureTierRule
{
    public override string RuleId => "data.schema-ref-match";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var findings = new List<ValidationFinding>();
        var nodesById = ctx.Definition.Nodes.ToDictionary(n => n.NodeId, StringComparer.Ordinal);

        foreach (var edge in ctx.Definition.Edges.Where(e => e.Kind == EdgeKind.Data && IsSchemaRef(e.ContractType)))
        {
            var edgeRef = ParseSchemaRef(edge.ContractType!);
            var edgeLabel = $"{edge.FromNodeId}->{edge.ToNodeId}";
            if (edgeRef is null)
            {
                findings.Add(new ValidationFinding(RuleId, DefaultSeverity,
                    $"'{edge.ContractType}' is not a well-formed schema ref — expected '{{namespace}}/{{name}}/{{major}}.{{minor}}' (ADR-E2).",
                    edge.FromNodeId, EdgeRef: edgeLabel));
                continue;
            }

            findings.AddRange(EndpointFindings(edge, edgeRef, edgeLabel, nodesById));
        }

        return findings;
    }

    /// <summary>Checks the producing node's output ref and the consuming node's input ref (where declared as schema refs) against the edge's — exact id, same major.</summary>
    internal IEnumerable<ValidationFinding> EndpointFindings(WorkflowEdge edge, SchemaRef edgeRef, string edgeLabel, IReadOnlyDictionary<string, WorkflowNode> nodesById)
    {
        if (nodesById.GetValueOrDefault(edge.FromNodeId) is AgentTaskNode producer && IsSchemaRef(producer.OutputContractType))
        {
            foreach (var finding in MatchFindings(producer.OutputContractType, edgeRef, producer.NodeId, "output_contract_type", edgeLabel))
            {
                yield return finding;
            }
        }

        if (nodesById.GetValueOrDefault(edge.ToNodeId) is AgentTaskNode consumer && IsSchemaRef(consumer.InputContractType))
        {
            foreach (var finding in MatchFindings(consumer.InputContractType, edgeRef, consumer.NodeId, "input_contract_type", edgeLabel))
            {
                yield return finding;
            }
        }
    }

    /// <summary>ADR-E2 D3: exact schema id, same major; minor may differ.</summary>
    internal IEnumerable<ValidationFinding> MatchFindings(string declared, SchemaRef edgeRef, string nodeId, string fieldPath, string edgeLabel)
    {
        var nodeRef = ParseSchemaRef(declared);
        if (nodeRef is null)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"'{declared}' is not a well-formed schema ref — expected '{{namespace}}/{{name}}/{{major}}.{{minor}}' (ADR-E2).",
                nodeId, FieldPath: fieldPath);
            yield break;
        }

        if (!string.Equals(nodeRef.Id, edgeRef.Id, StringComparison.Ordinal) || nodeRef.Major != edgeRef.Major)
        {
            yield return new ValidationFinding(RuleId, DefaultSeverity,
                $"schema ref '{declared}' does not match the edge's '{edgeRef.Id}/{edgeRef.Major}.{edgeRef.Minor}' — ADR-E2 requires the exact schema id and major version (minors may differ, majors never).",
                nodeId, FieldPath: fieldPath, EdgeRef: edgeLabel);
        }
    }

    /// <summary>A contract-type value naming a capability schema rather than a CLR contract (the ADR-E2 ref convention contains '/').</summary>
    internal static bool IsSchemaRef(string? contractType) =>
        contractType is not null && contractType.Contains('/', StringComparison.Ordinal);

    /// <summary>Parses <c>{namespace}/{name}/{major}.{minor}</c>; null when malformed.</summary>
    internal static SchemaRef? ParseSchemaRef(string value)
    {
        var match = SchemaRefPattern().Match(value);
        return match.Success
            ? new SchemaRef(
                $"{match.Groups["ns"].Value}/{match.Groups["name"].Value}",
                int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>An ADR-E2 schema ref: id (<c>{namespace}/{name}</c>) + major.minor.</summary>
    internal sealed record SchemaRef(string Id, int Major, int Minor);

    [GeneratedRegex(@"^(?<ns>[a-z0-9][a-z0-9\-.]*)/(?<name>[a-z0-9][a-z0-9\-]*)/(?<major>\d+)\.(?<minor>\d+)$")]
    private static partial Regex SchemaRefPattern();
}
