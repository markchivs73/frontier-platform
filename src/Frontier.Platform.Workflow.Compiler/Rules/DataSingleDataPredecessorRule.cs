
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// data.single-data-predecessor (doc 13 §4.2, ADR-5 Decision 3 — S13.7i): every node has
/// at most one inbound Data edge. Fan-in carries control, not data — the runtime delivers
/// exactly one upstream payload per node, and a multi-payload merge semantic is
/// deliberately not invented until a real workload needs it (the recorded trigger; the
/// ADR-E2 envelope is the expected carrier when it comes).
/// </summary>
public sealed class DataSingleDataPredecessorRule : PureTierRule
{
    public override string RuleId => "data.single-data-predecessor";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx) =>
        ctx.Definition.Edges
            .Where(edge => edge.Kind == EdgeKind.Data)
            .GroupBy(edge => edge.ToNodeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new ValidationFinding(RuleId, DefaultSeverity,
                $"node has {group.Count()} inbound Data edges (from {string.Join(", ", group.Select(edge => $"'{edge.FromNodeId}'").Order(StringComparer.Ordinal))}); the runtime delivers exactly one upstream payload, so declare a single Data-edge predecessor and converge the other branches with Control edges.",
                group.Key))
            .ToList();
}
