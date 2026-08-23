namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.unique-node-ids: every NodeId within the definition must be unique.
/// Doc 13 §4.2, Phase 1 rule catalogue.
/// </summary>
public sealed class StructureUniqueNodeIdsRule : PureTierRule
{
    public override string RuleId => "structure.unique-node-ids";
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();

        foreach (var node in ctx.Definition.Nodes)
        {
            if (!seen.Add(node.NodeId))
            {
                findings.Add(new ValidationFinding(
                    RuleId,
                    DefaultSeverity,
                    $"Duplicate node id '{node.NodeId}'.",
                    NodeId: node.NodeId));
            }
        }

        return findings;
    }
}
