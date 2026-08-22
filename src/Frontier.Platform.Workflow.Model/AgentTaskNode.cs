using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Invokes a MAF agent (doc 00 §3.2). Compiles to <c>InvokeAgentActivity</c>, which runs
/// the fixed pipeline: assemble context → validate input contract → resolve model role →
/// check guardrails → invoke → validate output contract (doc 00 §4.3).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record AgentTaskNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.AgentTask;

    /// <summary>The agent role to resolve via Model-Role Config (doc 08).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Reference to this agent's instructions (doc 14).</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("instructions_ref")]
    public required string InstructionsRef { get; init; }

    /// <summary>The wire type name of the contract this node's input must satisfy.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("input_contract_type")]
    public required string InputContractType { get; init; }

    /// <summary>The wire type name of the contract this node's output must satisfy.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("output_contract_type")]
    public required string OutputContractType { get; init; }

    /// <summary>Declares the context this agent needs; Context Assembly resolves it (doc 04).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("context_request")]
    public required ContextRequest ContextRequest { get; init; }

    /// <summary>
    /// MCP tool references this agent may call via MAF-native tool-calling (ADR-CD6). Each
    /// entry is <c>"{reverse-dns-server}/{tool}"</c> — the registered server name plus the
    /// tool as its last segment, e.g. <c>"com.example.crm/tickets/get_ticket"</c>
    /// (ADR-CD9, S13.7b; supersedes the S9.25 <c>connectors/</c> convention) — matching
    /// <c>ToolCall.Name</c>'s wire convention. Additive (canonical-serialization
    /// minor-change rule): defaults to empty, so existing published definitions rehydrate
    /// unaffected with no schema-version bump. References resolve against the resource
    /// registry's pinned card snapshots at design time and live <c>tools/list</c> at
    /// invocation time.
    /// </summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("tool_refs")]
    public IReadOnlyList<string> ToolRefs { get; init; } = [];
}
