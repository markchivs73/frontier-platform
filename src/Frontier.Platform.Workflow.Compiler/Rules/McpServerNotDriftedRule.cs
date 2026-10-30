using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// mcp.server-not-drifted (doc 13 §4.2; ADR-E27 D9): a new publish may not call a tool on a server
/// whose live <c>tools/list</c> the revalidation sweep flagged as drifted from its pinned snapshot.
/// The server is re-registered as a new version first. Pinned runs are unaffected: they validate
/// against the contracts their definition embeds.
/// </summary>
public sealed class McpServerNotDriftedRule : IDefinitionValidationRule
{
    private readonly IDesignerToolCatalog _toolCatalog;

    /// <summary>Constructs the rule over the registry's tools.</summary>
    public McpServerNotDriftedRule(IDesignerToolCatalog toolCatalog)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        _toolCatalog = toolCatalog;
    }

    public string RuleId => "mcp.server-not-drifted";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var drifted = (await _toolCatalog.GetToolsAsync(ct)).Where(t => t.ServerDrifted).ToDictionary(t => t.ToolRef, t => t.Server, StringComparer.Ordinal);
        var calls = ctx.Definition.Nodes.OfType<McpToolNode>().Select(n => (n.NodeId, Ref: n.ToolRef, Field: "tool_ref"))
            .Concat(ctx.Definition.Nodes.OfType<AgentTaskNode>().SelectMany(n => n.ToolRefs.Select(r => (n.NodeId, Ref: r, Field: "tool_refs"))));

        return calls
            .Where(c => drifted.ContainsKey(c.Ref))
            .Select(c => new ValidationFinding(RuleId, DefaultSeverity,
                $"tool '{c.Ref}' is on server '{drifted[c.Ref]}', whose live tool list has drifted from its registered snapshot. Re-register the server before publishing against it.",
                c.NodeId, FieldPath: c.Field))
            .ToList();
    }
}
