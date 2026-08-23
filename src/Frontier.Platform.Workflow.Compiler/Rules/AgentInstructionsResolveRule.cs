
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// agent.instructions-resolve (doc 13 §4.2 R2, S9.30): every <see cref="AgentTaskNode.InstructionsRef"/>
/// must resolve in the instructions store the runtime loads from — an unresolvable ref fails the
/// agent pipeline live at invocation (hit for real at S9.28's instructions-copy fix).
/// </summary>
public sealed class AgentInstructionsResolveRule : IDefinitionValidationRule
{
    private readonly IInstructionCatalog _instructions;

    /// <summary>Constructs the rule over the instruction catalogue.</summary>
    public AgentInstructionsResolveRule(IInstructionCatalog instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        _instructions = instructions;
    }

    public string RuleId => "agent.instructions-resolve";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var findings = new List<ValidationFinding>();
        foreach (var node in ctx.Definition.Nodes.OfType<AgentTaskNode>())
        {
            if (!await _instructions.ResolvesAsync(node.InstructionsRef, ct))
            {
                findings.Add(new ValidationFinding(RuleId, DefaultSeverity,
                    $"instructions_ref '{node.InstructionsRef}' does not resolve to a stored instruction.",
                    node.NodeId, FieldPath: "instructions_ref"));
            }
        }

        return findings;
    }
}
