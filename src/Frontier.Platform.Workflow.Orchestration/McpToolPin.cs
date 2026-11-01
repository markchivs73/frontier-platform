using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// What the published definition pins for one MCP tool a step calls (ADR-PA42): the snapshots of
/// the registry contracts the tool was designed against, read from
/// <see cref="Frontier.Platform.Workflow.Model.WorkflowDefinition.Contracts"/>, and its write flag.
/// <see cref="IMcpToolCatalog"/> builds the tool from these rather than from the newest active
/// registry version. Each field is independent: <see langword="null"/> means nothing is pinned for
/// that fact (a tool registered before contracts were data, a tool with no output schema, a
/// definition published before the flag), and the catalogue resolves it as it did before the pins.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip and wire tests.")]
public sealed record McpToolPin
{
    /// <summary>The tool this pins, <c>{reverse-dns-server}/{tool}</c> (ADR-CD9).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("tool_ref")]
    public required string ToolRef { get; init; }

    /// <summary>The pinned snapshot of the contract the tool's arguments satisfy; its schema and description are what the model is shown.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("input_contract")]
    public ContractSnapshot? InputContract { get; init; }

    /// <summary>The pinned snapshot of the contract the tool's result satisfies.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("output_contract")]
    public ContractSnapshot? OutputContract { get; init; }

    /// <summary>The pinned write flag: the sandbox write fence follows it rather than the newest registry version's.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("writes")]
    public bool? Writes { get; init; }
}
