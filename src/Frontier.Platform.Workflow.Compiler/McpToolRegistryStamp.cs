using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Copies what the registry holds for an MCP tool onto every <see cref="McpToolNode"/> that calls it
/// (ADR-PA41; ADR-PA39; consumer ADR-E27 D9, D11). The registry is the authority and the node holds a
/// copy, so nothing is merged: <c>input_contract_type</c>, <c>output_contract_type</c> and
/// <c>writes</c> are overwritten with the tool's <see cref="DesignerToolDescriptor"/> values, nulls
/// included. A node whose <c>tool_ref</c> the catalogue does not resolve is left as it is, for
/// <c>mcp.tool-resolves</c> to report.
/// </summary>
/// <remarks>
/// The chat designer applies this to every proposal the design agent returns, before it is validated,
/// diffed or merged. Publish never applies it: publish validates and refuses. It is public so a
/// consumer can apply the same rule wherever else it builds or rewrites a definition.
/// </remarks>
public static class McpToolRegistryStamp
{
    /// <summary>
    /// Returns <paramref name="definition"/> with each resolvable <see cref="McpToolNode"/>'s contract
    /// refs and write flag set to its tool's registered values. Every other node is returned as it is.
    /// When the catalogue lists a tool ref twice, the first entry wins, as in <c>mcp.tool-resolves</c>.
    /// </summary>
    /// <param name="definition">The definition to stamp; not modified.</param>
    /// <param name="tools">The tool catalogue's descriptors (<see cref="IDesignerToolCatalog"/>).</param>
    public static WorkflowDefinition Apply(WorkflowDefinition definition, IReadOnlyList<DesignerToolDescriptor> tools)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(tools);

        var byRef = tools
            .GroupBy(t => t.ToolRef, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return definition with { Nodes = definition.Nodes.Select(node => Stamp(node, byRef)).ToList() };
    }

    private static WorkflowNode Stamp(WorkflowNode node, Dictionary<string, DesignerToolDescriptor> byRef) =>
        node is McpToolNode toolNode && byRef.TryGetValue(toolNode.ToolRef, out var tool)
            ? toolNode with
            {
                InputContractType = tool.InputContractRef,
                OutputContractType = tool.OutputContractRef,
                Writes = tool.Writes,
            }
            : node;
}
