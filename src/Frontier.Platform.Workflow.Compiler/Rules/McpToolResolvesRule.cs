
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Resourced-tier rule (doc 13 §4.2, S9.27c; widened S13.7d): every <c>AgentTaskNode.ToolRefs</c>
/// entry AND every <c>McpToolNode.ToolRef</c> (designable since S13.7c) must resolve to a tool an
/// active registered MCP server's pinned card snapshot exposes (<see cref="IDesignerToolCatalog"/>,
/// ADR-CD9 — snapshot-backed since S13.7b) — the same catalogue the chat designer agent's
/// system prompt is constrained to, so a published definition can never reference a tool the
/// agent was never allowed to invent either.
/// </summary>
public sealed class McpToolResolvesRule : IDefinitionValidationRule
{
    private readonly IDesignerToolCatalog _toolCatalog;

    public McpToolResolvesRule(IDesignerToolCatalog toolCatalog)
    {
        ArgumentNullException.ThrowIfNull(toolCatalog);
        _toolCatalog = toolCatalog;
    }

    public string RuleId => "mcp.tool-resolves";
    public RuleTier Tier => RuleTier.Resourced;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public async Task<IReadOnlyList<ValidationFinding>> EvaluateAsync(DefinitionValidationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var tools = await _toolCatalog.GetToolsAsync(ct);
        var knownRefs = tools.Select(t => t.ToolRef).ToHashSet(StringComparer.Ordinal);

        var agentFindings = ctx.Definition.Nodes
            .OfType<AgentTaskNode>()
            .SelectMany(node => node.ToolRefs
                .Where(toolRef => !knownRefs.Contains(toolRef))
                .Select(toolRef => UnresolvedToolFinding(node.NodeId, toolRef)));

        // S13.7d: mcp_tool nodes became executable at S13.7c — their single ToolRef gets the
        // same pinned-snapshot resolution as agent tool_refs.
        var toolNodeFindings = ctx.Definition.Nodes
            .OfType<McpToolNode>()
            .Where(node => !knownRefs.Contains(node.ToolRef))
            .Select(node => UnresolvedToolFinding(node.NodeId, node.ToolRef));

        return agentFindings.Concat(toolNodeFindings).ToList();
    }

    private ValidationFinding UnresolvedToolFinding(string nodeId, string toolRef) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"tool_ref '{toolRef}' does not resolve to any tool exposed by an active registered MCP server",
        NodeId: nodeId,
        FieldPath: "tool_refs");
}
