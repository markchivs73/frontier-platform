using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Deterministic branch on a structured data predicate over prior outputs (doc 00 §3.2).
/// Evaluated purely in the interpreter; <see cref="DefaultBranchNodeId"/> guarantees no
/// unreachable fall-through (doc 13 §4.2 <c>graph.decision-edges</c>).
/// </summary>
/// <remarks>
/// S13.7j (ADR-5 Decision 6): doc 14 §6's <see cref="ConditionalPredicate"/> tree ships
/// additively as <see cref="Branches"/> — evaluated in order, first true condition routes
/// to its target, none true routes to <see cref="DefaultBranchNodeId"/>. The string
/// <see cref="Predicate"/> placeholder is deprecated and survives until a phase boundary
/// per the deprecation policy; a node carrying <see cref="Branches"/> is the executable
/// form (<c>determinism.predicates-compile</c> validates the tree).
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record DecisionNode : WorkflowNode
{
    /// <inheritdoc />
    [JsonIgnore]
    public override NodeType NodeType => NodeType.Decision;

    /// <summary>Legacy canonical string-encoded predicate. Deprecated at S13.7j — never evaluated; author <see cref="Branches"/> instead.</summary>
    [Obsolete("Author Branches (the doc 14 §6 ConditionalPredicate tree) instead; the string form was a placeholder and is never evaluated. Removed at the next phase boundary.")]
    [JsonPropertyOrder(3)]
    [JsonPropertyName("predicate")]
    public string? Predicate { get; init; }

    /// <summary>The node to follow when no other branch's condition matches.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("default_branch_node_id")]
    public required string DefaultBranchNodeId { get; init; }

    /// <summary>
    /// Ordered conditional branches (doc 14 §6, additive at S13.7j): the first branch
    /// whose condition evaluates true is taken; every target must be a Control-edge
    /// successor of this node. Absent on definitions authored before S13.7j (omit-null).
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("branches")]
    public IReadOnlyList<ConditionalBranch>? Branches { get; init; }
}
