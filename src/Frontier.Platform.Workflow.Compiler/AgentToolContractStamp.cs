using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Pins an agent node's tools (ADR-PA42): rebuilds <see cref="AgentTaskNode.ToolContracts"/> as one
/// entry per distinct <see cref="AgentTaskNode.ToolRefs"/> entry, in that order. A tool the catalogue
/// resolves gets its registered contract refs and write flag, nulls included; a tool it does not
/// resolve keeps whatever entry the node gave it, for <c>mcp.tool-resolves</c> to report. Entries for
/// tools the node no longer lists are dropped.
/// </summary>
internal static class AgentToolContractStamp
{
    /// <summary>
    /// <paramref name="node"/> with its tools pinned from <paramref name="byRef"/>. A node with no
    /// tools and no pins is returned as it is, so its bytes do not change; a node left with no pins has
    /// <c>tool_contracts</c> null rather than empty.
    /// </summary>
    internal static AgentTaskNode Apply(AgentTaskNode node, IReadOnlyDictionary<string, DesignerToolDescriptor> byRef)
    {
        var pins = node.ToolRefs
            .Distinct(StringComparer.Ordinal)
            .Select(toolRef => Pin(toolRef, node.ToolContracts, byRef))
            .OfType<AgentToolContract>()
            .ToList();
        if (pins.Count == 0 && node.ToolContracts is null)
        {
            return node;
        }

        return node with { ToolContracts = pins.Count == 0 ? null : pins };
    }

    /// <summary>The registry's pin for <paramref name="toolRef"/>, else the node's own entry for it, else null.</summary>
    internal static AgentToolContract? Pin(
        string toolRef, IReadOnlyList<AgentToolContract>? said, IReadOnlyDictionary<string, DesignerToolDescriptor> byRef) =>
        byRef.TryGetValue(toolRef, out var tool)
            ? new AgentToolContract
            {
                ToolRef = toolRef,
                InputContractType = tool.InputContractRef,
                OutputContractType = tool.OutputContractRef,
                Writes = tool.Writes,
            }
            : said?.FirstOrDefault(entry => string.Equals(entry.ToolRef, toolRef, StringComparison.Ordinal));
}
