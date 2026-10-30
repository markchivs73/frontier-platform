using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// A consumer-owned view of an MCP tool for the chat designer agent (doc 14 §3, §8; ADR-CD9).
/// Deliberately minimal — the design agent only needs to know a tool exists and what it's for,
/// so it can propose an <c>AgentTaskNode.ToolRefs</c> entry; the tool's input schema is MAF's
/// concern at invocation time (S9.25), not the design agent's concern at design time.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain projection DTO; values are exercised by the catalog adapter and chat-service tests.")]
public sealed record DesignerToolDescriptor
{
    /// <summary>The full reference an <c>AgentTaskNode.ToolRefs</c> entry uses, e.g. <c>"io.frontier.demo/autotask/get_new_ticket"</c> (ADR-CD9, S13.7b).</summary>
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>The registered MCP server's reverse-DNS name, e.g. <c>"io.frontier.demo/autotask"</c>.</summary>
    [JsonPropertyName("server")]
    public required string Server { get; init; }

    /// <summary>The tool's name as the MCP server declares it, e.g. <c>"get_new_ticket"</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>What the tool does — the agent matches designer intent against this.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The ref of the registry contract discovered for the tool's arguments (ADR-E27 D9). An
    /// <c>McpToolNode</c> calling this tool names exactly this ref (<c>mcp.tool-resolves</c>);
    /// <see langword="null"/> when the registry holds none.
    /// </summary>
    [JsonPropertyName("input_contract_ref")]
    public string? InputContractRef { get; init; }

    /// <summary>The ref of the registry contract discovered for the tool's result (ADR-E27 D9); <see langword="null"/> when the registry holds none.</summary>
    [JsonPropertyName("output_contract_ref")]
    public string? OutputContractRef { get; init; }

    /// <summary>
    /// Whether the revalidation sweep found the server's live <c>tools/list</c> drifted from its
    /// pinned snapshot (ADR-E27 D9). A new publish may not use it until it is re-registered
    /// (<c>mcp.server-not-drifted</c>). Governance only, so the design agent is never shown it.
    /// </summary>
    [JsonIgnore]
    public bool ServerDrifted { get; init; }
}

/// <summary>
/// Supplies the MCP tools the chat designer agent may propose for <c>AgentTaskNode.ToolRefs</c>
/// (doc 14 §3 <c>availableTools</c>; ADR-CD9: the registry's pinned card snapshots are the
/// contract source). A consumer-owned abstraction: the implementation reads the resource
/// registry and is wired only in the composition root, so the Definition Compiler stays
/// within its library boundary (no dependency on the Registry library or the MCP SDK).
/// </summary>
public interface IDesignerToolCatalog
{
    /// <summary>Returns the MCP tools available for <c>AgentTaskNode.ToolRefs</c> proposals, across every active registered server.</summary>
    Task<IReadOnlyList<DesignerToolDescriptor>> GetToolsAsync(CancellationToken ct);
}
