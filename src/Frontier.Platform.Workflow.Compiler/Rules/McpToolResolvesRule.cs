
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
        var byRef = tools.GroupBy(t => t.ToolRef, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

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

        // ADR-E27 D9 and ADR-PA41: an mcp_tool node names exactly the contracts the registry
        // discovered for its tool, and pins exactly the write flag the registry holds for it.
        var contractFindings = ctx.Definition.Nodes
            .OfType<McpToolNode>()
            .Where(node => byRef.ContainsKey(node.ToolRef))
            .SelectMany(node => ContractFindings(node, byRef[node.ToolRef]));

        return agentFindings.Concat(toolNodeFindings).Concat(contractFindings).ToList();
    }

    /// <summary>Where <paramref name="node"/>'s contract refs or write flag differ from the ones registered for its tool.</summary>
    internal IEnumerable<ValidationFinding> ContractFindings(McpToolNode node, DesignerToolDescriptor tool)
    {
        if (!string.Equals(node.InputContractType, tool.InputContractRef, StringComparison.Ordinal))
        {
            yield return ContractFinding(node, "input_contract_type", node.InputContractType, tool.InputContractRef);
        }

        if (!string.Equals(node.OutputContractType, tool.OutputContractRef, StringComparison.Ordinal))
        {
            yield return ContractFinding(node, "output_contract_type", node.OutputContractType, tool.OutputContractRef);
        }

        if (node.Writes != tool.Writes)
        {
            yield return RegistryFinding(node, "writes", WireBool(node.Writes), WireBool(tool.Writes), "Pin the tool's registered write flag.");
        }
    }

    private static string? WireBool(bool? value) => value is { } flag ? (flag ? "true" : "false") : null;

    private ValidationFinding ContractFinding(McpToolNode node, string field, string? named, string? registered) =>
        RegistryFinding(node, field, named, registered, "Name the tool's registered contract.");

    private ValidationFinding RegistryFinding(McpToolNode node, string field, string? named, string? registered, string remedy) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"{field} is '{named ?? "(none)"}', but the registry holds '{registered ?? "(none)"}' for tool '{node.ToolRef}'. {remedy}",
        NodeId: node.NodeId,
        FieldPath: field);

    private ValidationFinding UnresolvedToolFinding(string nodeId, string toolRef) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"tool_ref '{toolRef}' does not resolve to any tool exposed by an active registered MCP server",
        NodeId: nodeId,
        FieldPath: "tool_refs");
}
