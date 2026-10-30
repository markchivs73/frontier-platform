using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// One field of a data edge's projection (ADR-E27 D14): the consumer's input field
/// <see cref="Target"/> takes either the value at <see cref="From"/> in the producer's output or the
/// fixed <see cref="Literal"/>. Exactly one of the two is set. There are no expressions, functions or
/// conditions: logic belongs in nodes.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip/golden-file tests.")]
public sealed record ProjectionField
{
    /// <summary>The top-level property of the consumer's input this field fills, e.g. <c>"ticket_id"</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>An RFC 6901 JSON Pointer into the producer's output, e.g. <c>"/ticket/id"</c>. <c>""</c> is the whole output.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>A fixed JSON value for <see cref="Target"/>, used instead of <see cref="From"/>. Never JSON <c>null</c>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("literal")]
    public JsonElement? Literal { get; init; }
}
