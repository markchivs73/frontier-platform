using System.Text.Json;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Abstractions;

/// <summary>
/// A registry contract as it is pinned (ADR-E27 D4): its ref, its JSON Schema 2020-12 document, its
/// CEL rules and the hash over both. Immutable once registered; a change is a new version. A
/// definition embeds the snapshots it uses (ADR-E27 D15), so a run never fetches one.
/// <para>
/// <see cref="Hash"/> is <c>sha256:</c> plus the lower-case hex SHA-256 of the RFC 8785 (JCS) form of
/// <c>{"rules": [...], "schema": {...}}</c>, computed by <c>ContractHash</c> in
/// <c>Frontier.Platform.Contracts</c>. Lifecycle and provenance belong to the registry record that
/// holds the snapshot, not to the snapshot.
/// </para>
/// </summary>
public sealed record ContractSnapshot : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The ADR-E2 ref, <c>{namespace}/{name}/{major}.{minor}</c>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("contract_ref")]
    public required string ContractRef { get; init; }

    /// <summary>The JSON Schema 2020-12 document, exactly as registered.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("schema")]
    public required JsonElement Schema { get; init; }

    /// <summary>The CEL rules, in registration order. Empty when the contract has none.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("rules")]
    public required IReadOnlyList<ContractRule> Rules { get; init; }

    /// <summary>The content hash over <see cref="Schema"/> and <see cref="Rules"/>.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("hash")]
    public required string Hash { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(ContractRef))
        {
            violations.Add("contract_ref is required");
        }

        if (Schema.ValueKind != JsonValueKind.Object)
        {
            violations.Add("schema must be a JSON object");
        }

        if (Rules is null)
        {
            violations.Add("rules must not be null");
        }

        if (string.IsNullOrWhiteSpace(Hash) || !Hash.StartsWith("sha256:", StringComparison.Ordinal))
        {
            violations.Add("hash must be a sha256: content hash");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(ContractSnapshot), violations);
        }
    }
}
