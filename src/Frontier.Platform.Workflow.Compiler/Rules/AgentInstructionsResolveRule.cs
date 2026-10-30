using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// agent.instructions-resolve (doc 13 §4.2 R2, S9.30): every <see cref="AgentTaskNode.InstructionsRef"/>
/// a node names must resolve in the instructions store the runtime loads from — an unresolvable ref fails
/// the agent pipeline live at invocation (hit for real at S9.28's instructions-copy fix). ADR-PA40: a node
/// on an agent-backed role may omit the ref, because the agent brings its own instructions; a node on any
/// other role must name one. A blank ref is never accepted: omit it instead.
/// </summary>
public sealed class AgentInstructionsResolveRule : IDefinitionValidationRule
{
    private readonly IInstructionCatalog _instructions;
    private readonly IAgentRoleCatalog _roles;

    /// <summary>Constructs the rule over the instruction catalogue and the agent-role catalogue that says each role's backing.</summary>
    public AgentInstructionsResolveRule(IInstructionCatalog instructions, IAgentRoleCatalog roles)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(roles);
        _instructions = instructions;
        _roles = roles;
    }

    public string RuleId => "agent.instructions-resolve";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var agentBacked = (await _roles.GetAgentRolesAsync(ct))
            .Where(r => r.Backing == RoleBacking.Agent)
            .Select(r => r.RoleId)
            .ToHashSet(StringComparer.Ordinal);
        var findings = new List<ValidationFinding>();
        foreach (var node in ctx.Definition.Nodes.OfType<AgentTaskNode>())
        {
            if (await ProblemAsync(node, agentBacked.Contains(node.Role), ct) is { } message)
            {
                findings.Add(new ValidationFinding(RuleId, DefaultSeverity, message, node.NodeId, FieldPath: "instructions_ref"));
            }
        }

        return findings;
    }

    /// <summary>What is wrong with <paramref name="node"/>'s ref, or <see langword="null"/> when nothing is.</summary>
    private async Task<string?> ProblemAsync(AgentTaskNode node, bool agentBacked, CancellationToken ct) => node.InstructionsRef switch
    {
        null when agentBacked => null,
        null => $"role '{node.Role}' is not agent-backed, so the node must name an instructions_ref.",
        var blank when string.IsNullOrWhiteSpace(blank) => "instructions_ref is blank; name a stored instruction, or omit it on an agent-backed role.",
        var named when !await _instructions.ResolvesAsync(named, ct) => $"instructions_ref '{named}' does not resolve to a stored instruction.",
        _ => null,
    };
}
