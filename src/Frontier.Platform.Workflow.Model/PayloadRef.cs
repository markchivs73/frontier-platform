using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model;

/// <summary>
/// A reference to payload bytes held in staging storage (ADR-E1, DESIGN-DECISIONS.md S13.1):
/// refs ride the graph, bytes never do. The ref conveys <em>location and identity</em>, never
/// authority — access is granted per invocation via short-lived user-delegation SAS minted
/// activity-side (doc 15 ADR-SEC5), so the persisted URI must carry no token: any query
/// string is rejected by <see cref="Validate"/>. <see cref="ContentHash"/> pins the exact
/// bytes for retry determinism, dedupe, and the audit chain.
/// </summary>
public sealed record PayloadRef : IVersionedContract
{
    /// <inheritdoc />
    [JsonPropertyOrder(0)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Absolute storage URI of the payload bytes — location only, no query string (no SAS).</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("storage_uri")]
    public required Uri StorageUri { get; init; }

    /// <summary>SHA-256 of the payload bytes, as 64 lowercase hex characters.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    /// <summary>MIME content type of the payload, e.g. <c>"application/json"</c>.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("content_type")]
    public required string ContentType { get; init; }

    /// <summary>Size of the payload in bytes; must be positive.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("size_bytes")]
    public required long SizeBytes { get; init; }

    /// <inheritdoc />
    public void Validate()
    {
        var violations = new List<string>();

        if (!StorageUri.IsAbsoluteUri)
        {
            violations.Add("storage_uri must be an absolute URI.");
        }
        else if (!string.IsNullOrEmpty(StorageUri.Query))
        {
            violations.Add("storage_uri must not carry a query string — persisted refs never contain access tokens (ADR-SEC5).");
        }

        if (!IsSha256Hex(ContentHash))
        {
            violations.Add("content_hash must be 64 lowercase hex characters (SHA-256).");
        }

        if (string.IsNullOrWhiteSpace(ContentType))
        {
            violations.Add("content_type must not be empty.");
        }

        if (SizeBytes <= 0)
        {
            violations.Add("size_bytes must be positive.");
        }

        if (violations.Count > 0)
        {
            throw new ContractViolationException(nameof(PayloadRef), violations);
        }
    }

    /// <summary>True when <paramref name="value"/> is exactly 64 lowercase hex characters.</summary>
    internal static bool IsSha256Hex(string value)
    {
        if (value is not { Length: 64 })
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
