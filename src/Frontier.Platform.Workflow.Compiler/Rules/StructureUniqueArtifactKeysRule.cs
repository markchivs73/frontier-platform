using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// structure.unique-artifact-keys (S13.199): no two nodes may produce the same section. The
/// orchestrator keys section state and artifact versions by <c>artifact_key</c> alone, so two
/// nodes sharing one would overwrite each other's output and leave the earlier step showing
/// none. A node with no <c>artifact_key</c> produces no section and is not compared. Keys are
/// compared ordinally, the way the orchestrator looks them up.
/// </summary>
public sealed class StructureUniqueArtifactKeysRule : PureTierRule
{
    /// <inheritdoc />
    public override string RuleId => "structure.unique-artifact-keys";

    /// <inheritdoc />
    public override ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    protected override IReadOnlyList<ValidationFinding> ValidateStructural(DefinitionValidationContext ctx)
    {
        var firstProducer = new Dictionary<string, string>(StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();

        foreach (var node in ctx.Definition.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.ArtifactKey)))
        {
            if (!firstProducer.TryAdd(node.ArtifactKey!, node.NodeId))
            {
                findings.Add(DuplicateFinding(node, firstProducer[node.ArtifactKey!]));
            }
        }

        return findings;
    }

    private ValidationFinding DuplicateFinding(WorkflowNode node, string firstNodeId) => new(
        RuleId,
        DefaultSeverity,
        $"artifact_key '{node.ArtifactKey}' is already produced by node '{firstNodeId}'; each section needs its own key, or one step's output overwrites the other's.",
        node.NodeId,
        FieldPath: "artifact_key");
}
