using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// The <c>audit-telemetry-staging</c> container's document shape (doc 05 §9, C-14): wraps
/// a staged <see cref="AuditTelemetryRecord"/> with the Cosmos-only <see cref="Id"/> and
/// the <c>/execution_id</c> partition key. The container's 30-day default TTL (doc 05 §9)
/// applies to every document here — staging is disposable once the signed audit record
/// for the execution exists.
/// </summary>
internal sealed record AuditTelemetryStagingDocument
{
    /// <summary>The deterministic document id: <c>{executionId}:{correlationId}</c> (doc 05 §9).</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key (doc 05 §9) — mirrors <see cref="AuditTelemetryRecord.ExecutionId"/>.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("execution_id")]
    public required string ExecutionId { get; init; }

    /// <summary>The staged invocation telemetry.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("record")]
    public required AuditTelemetryRecord Record { get; init; }

    /// <summary>Wraps <paramref name="record"/> for storage under its deterministic id.</summary>
    internal static AuditTelemetryStagingDocument FromRecord(AuditTelemetryRecord record) => new()
    {
        Id = AuditTelemetryDocumentId.ForInvocation(record.ExecutionId, record.CorrelationId),
        ExecutionId = record.ExecutionId,
        Record = record,
    };
}
