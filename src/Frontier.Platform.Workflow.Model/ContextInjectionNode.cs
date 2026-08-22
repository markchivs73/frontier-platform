using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Deprecated (doc 00 §3.2, ADR-CR1): dynamic-tier context refresh is now signal-driven —
/// the Sense layer detects engagement-context change and signals the orchestrator, which
/// decides whether and when to refresh. Kept for backward compatibility with definitions
/// authored before ADR-CR1; new workflows should rely on signal events instead.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
[Obsolete("Dynamic context refresh is signal-driven (ADR-CR1); new workflows should not place ContextInjectionNode.")]
public sealed record ContextInjectionNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.ContextInjection;

    /// <summary>The context request to (re-)assemble when this node executes.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("context_request")]
    public required ContextRequest ContextRequest { get; init; }
}
