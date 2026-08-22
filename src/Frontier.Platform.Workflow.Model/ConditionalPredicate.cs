using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// Structured decision predicate (doc 14 §6, ADR-CD4; pulled forward at S13.7j per ADR-5
/// Decision 6): decision logic is data the design agent composes — never an expression
/// string, never parsed or compiled user code. Evaluated purely in the orchestrator body
/// with fixed semantics (invariant culture, ordinal strings, declared-scale decimals,
/// ISO-8601 dates) — replay-safe by construction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FieldComparisonPredicate), "field")]
[JsonDerivedType(typeof(LogicalPredicate), "logical")]
public abstract record ConditionalPredicate;

/// <summary>
/// Compares one field of an upstream section's output against a canonical string-encoded
/// value (doc 14 §6). <see cref="FieldPath"/> is <c>{artifact_key}.{property.path}</c> —
/// the first segment names the producing section, the rest walk the contract's wire
/// (snake_case) property names.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record FieldComparisonPredicate : ConditionalPredicate
{
    /// <summary>Artifact key + wire property path, e.g. <c>"scope.budget"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("field_path")]
    public required string FieldPath { get; init; }

    /// <summary>The comparison to apply.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("operator")]
    public required ComparisonOp Operator { get; init; }

    /// <summary>Canonical string encoding of the comparand (profile rules: string decimals, ISO dates). Null only for <see cref="ComparisonOp.In"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>Comparand set for <see cref="ComparisonOp.In"/>; null for every other operator.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("values")]
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>Combines predicates with <see cref="LogicalOp"/> semantics (doc 14 §6): And/Or take one or more operands, Not exactly one.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record LogicalPredicate : ConditionalPredicate
{
    /// <summary>The combinator.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("op")]
    public required LogicalOp Op { get; init; }

    /// <summary>The combined predicates, evaluated in order.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("operands")]
    public required IReadOnlyList<ConditionalPredicate> Operands { get; init; }
}

/// <summary>One ordered branch of a <see cref="DecisionNode"/> (doc 14 §6): the first branch whose condition evaluates true selects <see cref="TargetNodeId"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6 round-trip/golden-file tests.")]
public sealed record ConditionalBranch
{
    /// <summary>The node this branch routes to; must be a Control-edge successor of the decision node.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("target_node_id")]
    public required string TargetNodeId { get; init; }

    /// <summary>The branch's condition.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("condition")]
    public required ConditionalPredicate Condition { get; init; }
}

/// <summary>Comparison operators for <see cref="FieldComparisonPredicate"/> (doc 14 §6). Serializes as a snake_case string like any enum.</summary>
public sealed class ComparisonOp : SmartEnum<ComparisonOp>
{
    /// <summary>Greater than (decimal/date comparands).</summary>
    public static readonly ComparisonOp Gt = new("gt");

    /// <summary>Less than (decimal/date comparands).</summary>
    public static readonly ComparisonOp Lt = new("lt");

    /// <summary>Greater than or equal (decimal/date comparands).</summary>
    public static readonly ComparisonOp Gte = new("gte");

    /// <summary>Less than or equal (decimal/date comparands).</summary>
    public static readonly ComparisonOp Lte = new("lte");

    /// <summary>Equal (decimal/date when both coerce, else ordinal string).</summary>
    public static readonly ComparisonOp Eq = new("eq");

    /// <summary>Not equal (decimal/date when both coerce, else ordinal string).</summary>
    public static readonly ComparisonOp Neq = new("neq");

    /// <summary>Field value is a member of <see cref="FieldComparisonPredicate.Values"/> (ordinal).</summary>
    public static readonly ComparisonOp In = new("in");

    /// <summary>String field contains the comparand (ordinal).</summary>
    public static readonly ComparisonOp Contains = new("contains");

    /// <summary>String field starts with the comparand (ordinal).</summary>
    public static readonly ComparisonOp StartsWith = new("starts_with");

    /// <summary>String field ends with the comparand (ordinal).</summary>
    public static readonly ComparisonOp EndsWith = new("ends_with");

    private ComparisonOp(string name)
        : base(name)
    {
    }
}

/// <summary>Logical combinators for <see cref="LogicalPredicate"/> (doc 14 §6). Serializes as a snake_case string like any enum.</summary>
public sealed class LogicalOp : SmartEnum<LogicalOp>
{
    /// <summary>Every operand must evaluate true.</summary>
    public static readonly LogicalOp And = new("and");

    /// <summary>At least one operand must evaluate true.</summary>
    public static readonly LogicalOp Or = new("or");

    /// <summary>Inverts its single operand.</summary>
    public static readonly LogicalOp Not = new("not");

    private LogicalOp(string name)
        : base(name)
    {
    }
}
