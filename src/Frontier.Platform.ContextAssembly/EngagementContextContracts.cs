using System.Text.Json.Serialization;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Cosmos container <c>engagement-context</c> document shape (epoch): immutable
/// snapshot of dynamic context at a given epoch, partition key <c>/engagementId</c>,
/// id <c>{engagementId}:ctx:e{epoch:D6}</c> (S6.2a, doc 04 §6).
/// </summary>
public sealed record EngagementContextEpoch(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)]
    string Id,

    [property: JsonPropertyName("engagement_id"), JsonPropertyOrder(1)]
    string EngagementId,

    [property: JsonPropertyName("epoch"), JsonPropertyOrder(2)]
    int Epoch,

    [property: JsonPropertyName("content_hash"), JsonPropertyOrder(3)]
    string ContentHash,

    [property: JsonPropertyName("content"), JsonPropertyOrder(4)]
    string Content,

    [property: JsonPropertyName("created_at_utc"), JsonPropertyOrder(5)]
    DateTime CreatedAtUtc) : IVersionedContract
{
    /// <inheritdoc />
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ContractViolationException("EngagementContextEpoch.Id is required.");
        if (string.IsNullOrWhiteSpace(EngagementId))
            throw new ContractViolationException("EngagementContextEpoch.EngagementId is required.");
        if (Epoch < 0)
            throw new ContractViolationException("EngagementContextEpoch.Epoch cannot be negative.");
        if (string.IsNullOrWhiteSpace(ContentHash))
            throw new ContractViolationException("EngagementContextEpoch.ContentHash is required.");
        if (string.IsNullOrWhiteSpace(Content))
            throw new ContractViolationException("EngagementContextEpoch.Content is required.");
    }

    /// <inheritdoc />
    public string SchemaVersion => "1";
}

/// <summary>
/// Cosmos container <c>engagement-context</c> document shape (current pointer):
/// mutable pointer to the active epoch, partition key <c>/engagementId</c>,
/// id <c>{engagementId}:ctx:current</c> (S6.2a, doc 04 §6).
/// </summary>
public sealed record EngagementContextPointer(
    [property: JsonPropertyName("id"), JsonPropertyOrder(0)]
    string Id,

    [property: JsonPropertyName("engagement_id"), JsonPropertyOrder(1)]
    string EngagementId,

    [property: JsonPropertyName("epoch"), JsonPropertyOrder(2)]
    int Epoch,

    [property: JsonPropertyName("content_hash"), JsonPropertyOrder(3)]
    string ContentHash,

    [property: JsonPropertyName("updated_at_utc"), JsonPropertyOrder(4)]
    DateTime UpdatedAtUtc) : IVersionedContract
{
    /// <inheritdoc />
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ContractViolationException("EngagementContextPointer.Id is required.");
        if (string.IsNullOrWhiteSpace(EngagementId))
            throw new ContractViolationException("EngagementContextPointer.EngagementId is required.");
        if (Epoch < 0)
            throw new ContractViolationException("EngagementContextPointer.Epoch cannot be negative.");
        if (string.IsNullOrWhiteSpace(ContentHash))
            throw new ContractViolationException("EngagementContextPointer.ContentHash is required.");
    }

    /// <inheritdoc />
    public string SchemaVersion => "1";
}
