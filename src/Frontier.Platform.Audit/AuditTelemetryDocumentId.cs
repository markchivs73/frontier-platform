namespace Frontier.Platform.Audit;

/// <summary>
/// Deterministic Cosmos document <c>id</c> formatting for the <c>audit-telemetry-staging</c>
/// container (doc 05 §9): <c>{executionId}:{correlationId}</c> — one document per agent
/// invocation, so a retried <see cref="IAuditTelemetryStaging.RecordInvocationAsync"/>
/// upserts the same document (cosmos-conventions: convergent, not duplicating).
/// </summary>
internal static class AuditTelemetryDocumentId
{
    /// <summary>Builds the staging document id for the invocation identified by <paramref name="correlationId"/>.</summary>
    internal static string ForInvocation(string executionId, string correlationId) =>
        $"{executionId}:{correlationId}";
}
