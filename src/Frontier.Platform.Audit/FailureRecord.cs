using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Why a failed execution stopped (S13.240, ADR-PA46): the doc 10 §3 classification and, when the
/// final checkpoint recorded it, the failing node and the bounded, secret-free failure the projection
/// carries (<c>ExecutionSnapshot.failure_detail</c>, S13.120). Copied from the final snapshot by the
/// consolidator and covered by <c>record_hash</c>.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by round-trip/golden-file tests.")]
public sealed record FailureRecord
{
    /// <summary>The doc 10 §3 reason code, such as <c>contract_violation</c>, <c>guardrail</c> or <c>unclassified</c>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    /// <summary>The node whose failure stopped the run; omitted when the checkpoint did not record it.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    /// <summary>The failure's exception type name; omitted when the checkpoint did not record it.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("exception_type")]
    public string? ExceptionType { get; init; }

    /// <summary>The failure's bounded message, as the checkpoint recorded it; omitted when it did not.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
