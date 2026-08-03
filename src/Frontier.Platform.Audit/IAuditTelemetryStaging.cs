
namespace Frontier.Platform.Audit;

/// <summary>
/// Per-invocation telemetry staging for the audit trail (doc 05 §9, C-14): the
/// agent-task activity pipeline writes one <see cref="AuditTelemetryRecord"/> here per
/// invocation, directly — in place of doc 05's OTEL-collector-to-staging hop — and the
/// audit consolidator (S5.4) reads them back by execution to build the execution's agent
/// invocation list and aggregate cache metrics (C-15).
/// </summary>
public interface IAuditTelemetryStaging
{
    /// <summary>
    /// Upserts <paramref name="record"/> under its deterministic staging document id
    /// (doc 05 §9) — idempotent under activity retry (cosmos-conventions).
    /// </summary>
    Task RecordInvocationAsync(AuditTelemetryRecord record, CancellationToken cancellationToken);

    /// <summary>Reads every staged invocation recorded for <paramref name="executionId"/>.</summary>
    Task<IReadOnlyList<AuditTelemetryRecord>> GetForExecutionAsync(string executionId, CancellationToken cancellationToken);
}
