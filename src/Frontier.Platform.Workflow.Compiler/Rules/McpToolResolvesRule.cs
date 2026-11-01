
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// Resourced-tier rule (doc 13 §4.2, S9.27c; widened S13.7d): every <c>AgentTaskNode.ToolRefs</c>
/// entry AND every <c>McpToolNode.ToolRef</c> (designable since S13.7c) must resolve to a tool an
/// active registered MCP server's pinned card snapshot exposes (<see cref="IDesignerToolCatalog"/>,
/// ADR-CD9 — snapshot-backed since S13.7b) — the same catalogue the chat designer agent's
/// system prompt is constrained to, so a published definition can never reference a tool the
/// agent was never allowed to invent either. Each resolved tool is also held to the contract refs
/// and write flag the registry holds for it: an <c>McpToolNode</c>'s own fields (ADR-E27 D9,
/// ADR-PA41), and an agent node's <c>tool_contracts</c> entry, one per tool (ADR-PA42).
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
        var byRef = tools.GroupBy(t => t.ToolRef, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var agents = ctx.Definition.Nodes.OfType<AgentTaskNode>().ToList();
        var toolNodes = ctx.Definition.Nodes.OfType<McpToolNode>().ToList();

        return UnresolvedFindings(agents, toolNodes, byRef)
            .Concat(toolNodes.Where(node => byRef.ContainsKey(node.ToolRef)).SelectMany(node => ContractFindings(node, byRef[node.ToolRef])))
            .Concat(agents.SelectMany(node => ToolContractFindings(node, byRef)))
            .ToList();
    }

    /// <summary>
    /// Every agent <c>tool_refs</c> entry and every <c>mcp_tool</c> <c>tool_ref</c> the catalogue does
    /// not resolve. S13.7d: mcp_tool nodes became executable at S13.7c, so their single ToolRef gets the
    /// same pinned-snapshot resolution as agent tool_refs.
    /// </summary>
    internal IEnumerable<ValidationFinding> UnresolvedFindings(
        IEnumerable<AgentTaskNode> agents, IEnumerable<McpToolNode> toolNodes, IReadOnlyDictionary<string, DesignerToolDescriptor> byRef) =>
        agents
            .SelectMany(node => node.ToolRefs.Where(toolRef => !byRef.ContainsKey(toolRef)).Select(toolRef => UnresolvedToolFinding(node.NodeId, toolRef)))
            .Concat(toolNodes.Where(node => !byRef.ContainsKey(node.ToolRef)).Select(node => UnresolvedToolFinding(node.NodeId, node.ToolRef)));

    /// <summary>
    /// ADR-E27 D9 and ADR-PA41: where an mcp_tool <paramref name="node"/>'s contract refs or write flag
    /// differ from the ones registered for its tool.
    /// </summary>
    internal IEnumerable<ValidationFinding> ContractFindings(McpToolNode node, DesignerToolDescriptor tool) =>
        PinFindings(node.NodeId, string.Empty, PinOf(node), tool);

    /// <summary>
    /// ADR-PA42: where an agent <paramref name="node"/>'s <c>tool_contracts</c> fail to pin each
    /// resolvable <c>tool_refs</c> entry exactly once with its registered contract refs and write flag,
    /// or pin a tool the node does not list. An unresolvable tool_ref is reported once, as unresolved.
    /// </summary>
    internal IEnumerable<ValidationFinding> ToolContractFindings(AgentTaskNode node, IReadOnlyDictionary<string, DesignerToolDescriptor> byRef)
    {
        var entries = node.ToolContracts ?? [];
        var listed = node.ToolRefs.ToHashSet(StringComparer.Ordinal);
        var strays = entries
            .Select((entry, index) => (entry, index))
            .Where(e => !listed.Contains(e.entry.ToolRef) || IndexOfPin(entries, e.entry.ToolRef) != e.index)
            .Select(e => StrayPinFinding(node.NodeId, e.index, e.entry.ToolRef));
        var pins = node.ToolRefs
            .Distinct(StringComparer.Ordinal)
            .Where(byRef.ContainsKey)
            .SelectMany(toolRef => EntryFindings(node.NodeId, toolRef, entries, byRef[toolRef]));
        return strays.Concat(pins);
    }

    /// <summary>The finding for a resolvable <paramref name="toolRef"/> with no entry, else the findings for its first entry.</summary>
    internal IEnumerable<ValidationFinding> EntryFindings(
        string nodeId, string toolRef, IReadOnlyList<AgentToolContract> entries, DesignerToolDescriptor tool)
    {
        var index = IndexOfPin(entries, toolRef);
        return index < 0
            ? [MissingPinFinding(nodeId, toolRef)]
            : PinFindings(nodeId, $"tool_contracts[{index}].", entries[index], tool);
    }

    /// <summary>Where <paramref name="pinned"/>'s contract refs or write flag differ from <paramref name="tool"/>'s registered ones.</summary>
    internal IEnumerable<ValidationFinding> PinFindings(string nodeId, string fieldPrefix, AgentToolContract pinned, DesignerToolDescriptor tool)
    {
        if (!string.Equals(pinned.InputContractType, tool.InputContractRef, StringComparison.Ordinal))
        {
            yield return ContractFinding(nodeId, tool.ToolRef, fieldPrefix + "input_contract_type", pinned.InputContractType, tool.InputContractRef);
        }

        if (!string.Equals(pinned.OutputContractType, tool.OutputContractRef, StringComparison.Ordinal))
        {
            yield return ContractFinding(nodeId, tool.ToolRef, fieldPrefix + "output_contract_type", pinned.OutputContractType, tool.OutputContractRef);
        }

        if (pinned.Writes != tool.Writes)
        {
            yield return RegistryFinding(nodeId, tool.ToolRef, fieldPrefix + "writes", WireBool(pinned.Writes), WireBool(tool.Writes), "Pin the tool's registered write flag.");
        }
    }

    private static AgentToolContract PinOf(McpToolNode node) => new()
    {
        ToolRef = node.ToolRef,
        InputContractType = node.InputContractType,
        OutputContractType = node.OutputContractType,
        Writes = node.Writes,
    };

    private static int IndexOfPin(IReadOnlyList<AgentToolContract> entries, string toolRef) =>
        entries.Select(entry => entry.ToolRef).ToList().FindIndex(r => string.Equals(r, toolRef, StringComparison.Ordinal));

    private static string? WireBool(bool? value) => value is { } flag ? (flag ? "true" : "false") : null;

    private ValidationFinding ContractFinding(string nodeId, string toolRef, string field, string? named, string? registered) =>
        RegistryFinding(nodeId, toolRef, field, named, registered, "Name the tool's registered contract.");

    private ValidationFinding RegistryFinding(string nodeId, string toolRef, string field, string? named, string? registered, string remedy) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"{field} is '{named ?? "(none)"}', but the registry holds '{registered ?? "(none)"}' for tool '{toolRef}'. {remedy}",
        NodeId: nodeId,
        FieldPath: field);

    private ValidationFinding MissingPinFinding(string nodeId, string toolRef) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"tool_ref '{toolRef}' has no tool_contracts entry. Pin the tool's registered contracts and write flag.",
        NodeId: nodeId,
        FieldPath: "tool_contracts");

    private ValidationFinding StrayPinFinding(string nodeId, int index, string toolRef) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"tool_contracts[{index}] pins tool '{toolRef}', which tool_refs does not list or an earlier entry already pins. Pin each tool_refs entry once.",
        NodeId: nodeId,
        FieldPath: $"tool_contracts[{index}]");

    private ValidationFinding UnresolvedToolFinding(string nodeId, string toolRef) => new(
        RuleId: RuleId,
        Severity: DefaultSeverity,
        Message: $"tool_ref '{toolRef}' does not resolve to any tool exposed by an active registered MCP server",
        NodeId: nodeId,
        FieldPath: "tool_refs");
}
