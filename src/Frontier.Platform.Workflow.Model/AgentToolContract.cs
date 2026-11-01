using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// What an agent node pins for one of its <see cref="AgentTaskNode.ToolRefs"/> (ADR-PA42): the
/// registry contracts the tool was registered with and whether it writes, copied from the tool's
/// registry record when the node is designed and held to it by <c>mcp.tool-resolves</c>, as an
/// <see cref="McpToolNode"/> pins the same three facts for its one tool (ADR-PA41). A tool the
/// registry holds no contract or write flag for pins null in that field, as an
/// <see cref="McpToolNode"/> does.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip/golden-file tests.")]
public sealed record AgentToolContract
{
    /// <summary>The <see cref="AgentTaskNode.ToolRefs"/> entry this pins, e.g. <c>"com.example.crm/tickets/get_ticket"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>The ADR-E2 ref of the registry contract the tool's arguments satisfy; null when the registry holds none.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("input_contract_type")]
    public string? InputContractType { get; init; }

    /// <summary>The ADR-E2 ref of the registry contract the tool's result satisfies; null when the tool declares no output schema.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("output_contract_type")]
    public string? OutputContractType { get; init; }

    /// <summary>Whether the registry records the tool as mutating its connector's state; null when it holds no value.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("writes")]
    public bool? Writes { get; init; }
}
