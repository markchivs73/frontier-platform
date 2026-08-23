using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Input to <see cref="AgentTaskActivity"/> — the fields of an <see cref="Abstractions.AgentTaskNode"/>
/// needed to run the full <c>InvokeAgentActivity</c> pipeline (doc 00 §4.3, S4.2), plus a
/// deterministic <see cref="CorrelationId"/> minted by <see cref="GraphOrchestratorSteps"/>.
/// </summary>
public sealed record AgentTaskActivityInput
{
    /// <summary>The node's <see cref="Abstractions.WorkflowNode.NodeId"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("node_id")]
    public required string NodeId { get; init; }

    /// <summary>The section this node produces output for, if any.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("artifact_key")]
    public string? ArtifactKey { get; init; }

    /// <summary>The agent role to resolve via Model-Role Config (doc 08).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Reference to this agent's instructions (doc 14 placeholder — S4.1's <c>instructions/*.md</c> files).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("instructions_ref")]
    public required string InstructionsRef { get; init; }

    /// <summary>The wire type name of the contract this node's input must satisfy.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("input_contract_type")]
    public required string InputContractType { get; init; }

    /// <summary>The wire type name of the contract this node's output must satisfy.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("output_contract_type")]
    public required string OutputContractType { get; init; }

    /// <summary>Idempotency and audit join key for this step's activity invocation.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    /// <summary>The engagement this execution belongs to (Model-Role Config canary assignment, Context Assembly dynamic tier).</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The node's context request (doc 03 §2): which baseline/dynamic/real-time sources Context Assembly composes.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName("context_request")]
    public required ContextRequest ContextRequest { get; init; }

    /// <summary>
    /// The canonical-JSON output payload of this node's upstream Data-edge predecessor, or
    /// <see langword="null"/> if <see cref="InputContractType"/> has no predecessor (e.g.
    /// <c>gen-scope</c>, whose input is built from the dynamic context tier instead — S4.1).
    /// </summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName("upstream_payload")]
    public string? UpstreamPayload { get; init; }

    /// <summary>The running execution's instance id (<c>context.InstanceId</c>), needed to build <see cref="Frontier.Platform.Guardrails.InvocationCostEstimate.ExecutionId"/> (S4.2).</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>
    /// The reject-with-note text from the gate rejection that triggered this regeneration,
    /// or <see langword="null"/> outside a rollback cascade (doc 06 §13, S4.6). Surfaced to
    /// the agent via the <c>"hitl-revision-note"</c> real-time source when
    /// <see cref="ContextRequest.RealTimeSources"/> requests it.
    /// </summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName("revision_note")]
    public string? RevisionNote { get; init; }

    /// <summary>Mirrors <see cref="Abstractions.AgentTaskNode.ToolRefs"/> (ADR-CD6, S9.25).</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName("tool_refs")]
    public IReadOnlyList<string> ToolRefs { get; init; } = [];
}
