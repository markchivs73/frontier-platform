using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// The <c>audit-records</c> container's document shape (doc 02 §3, doc 05 §6): wraps a
/// <see cref="SignedAuditRecord"/> with the Cosmos-only <see cref="Id"/> and the
/// <c>/engagement_id</c> partition key.
///
/// <para>
/// Every property here sits <em>outside</em> <see cref="Record"/> and is therefore not signed —
/// which is what lets ADR-PA31 add <see cref="DocType"/> without changing one byte of a signed
/// record or bumping its schema version.
/// </para>
/// </summary>
internal sealed record SignedAuditRecordDocument
{
    /// <summary>The deterministic document id: <c>{executionId}:audit</c> (doc 05 §6).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 05 §6) — mirrors <see cref="SignedAuditRecord.EngagementId"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("engagement_id")]
    public required string EngagementId { get; init; }

    /// <summary>The signed audit record.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record")]
    public required SignedAuditRecord Record { get; init; }

    /// <summary>Doc 13 §5: a sandbox run's record purges after seven days; it is not an evidential record.</summary>
    internal const int SandboxTtlSeconds = 7 * 24 * 60 * 60;

    /// <summary>A real run's governance record never expires.</summary>
    internal const int NeverExpires = -1;

    /// <summary>
    /// The per-item Cosmos TTL in seconds: <see cref="SandboxTtlSeconds"/> for a sandbox record,
    /// <see cref="NeverExpires"/> otherwise. It sits outside <see cref="Record"/>, so it is not signed.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("ttl")]
    public required int Ttl { get; init; }

    /// <summary>
    /// <see cref="AuditRecordDocumentId.RecordDocType"/> — what distinguishes a record from the
    /// engagement's chain head, which shares this container and partition (ADR-PA31). Records stored
    /// before ADR-PA31 have no <c>doc_type</c>; readers treat its absence as "record".
    /// </summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("doc_type")]
    public string DocType { get; init; } = AuditRecordDocumentId.RecordDocType;

    /// <summary>Wraps <paramref name="record"/> for storage under its deterministic id.</summary>
    internal static SignedAuditRecordDocument FromRecord(SignedAuditRecord record) => new()
    {
        Id = AuditRecordDocumentId.ForExecution(record.ExecutionId),
        EngagementId = record.EngagementId,
        Record = record,
        Ttl = record.Sandbox == true ? SandboxTtlSeconds : NeverExpires,
    };
}
