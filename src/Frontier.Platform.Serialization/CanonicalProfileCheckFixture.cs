using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Serialization;

/// <summary>
/// Fixture for <see cref="CanonicalProfileCheck"/> (doc 12 §6): a small POCO exercising
/// the parts of <see cref="CanonicalProfile"/> that definition-hash identity, cache
/// keys, and audit signing depend on — explicit property order, omit-null, the
/// fixed-precision decimal converter, and the ISO-8601-UTC-ms date converter. Its
/// canonical bytes hash to <see cref="CanonicalProfileCheck.ExpectedFixtureHashHex"/>;
/// any drift in the shared profile changes that hash.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data fixture; exercised by CanonicalProfileCheck.")]
internal sealed record CanonicalProfileCheckFixture
{
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyOrder(1)]
    [JsonPropertyName("amount")]
    [DecimalPrecision(2)]
    public decimal Amount { get; init; } = 1250m;

    [JsonPropertyOrder(2)]
    [JsonPropertyName("checkpointed_at_utc")]
    public DateTime CheckpointedAtUtc { get; init; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [JsonPropertyOrder(3)]
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
