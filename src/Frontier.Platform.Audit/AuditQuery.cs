using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Filter criteria for the audit query service's governance queries (doc 05 §2, §7).
/// Every field is optional; an unset field does not filter on that dimension.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract; properties are exercised by S1.6-style round-trip/golden-file tests.")]
public sealed record AuditQuery
{
    /// <summary>Restrict to one engagement's chain.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("engagement_id")]
    public string? EngagementId { get; init; }

    /// <summary>Restrict to invocations resolved to this model id (doc 05 §7 query 1).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    /// <summary>Restrict to records containing an outcome from this validator (doc 05 §7 query 2).</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("validator_id")]
    public string? ValidatorId { get; init; }

    /// <summary>Restrict to records containing a human override decision (doc 05 §7 query 4).</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("overrides_only")]
    public bool OverridesOnly { get; init; }

    /// <summary>Restrict to records closed on or after this UTC instant.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("from_utc")]
    public DateTime? FromUtc { get; init; }

    /// <summary>Restrict to records closed on or before this UTC instant.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName("to_utc")]
    public DateTime? ToUtc { get; init; }

    /// <summary>Restrict to records produced by this exact workflow definition (doc 05 §7 query 8).</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName("definition_hash")]
    public string? DefinitionHash { get; init; }
}
