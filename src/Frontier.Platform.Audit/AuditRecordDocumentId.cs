namespace Frontier.Platform.Audit;

/// <summary>
/// Deterministic Cosmos document <c>id</c> formatting for the <c>audit-records</c> container
/// (doc 05 §6): <c>{executionId}:audit</c> — one signed record per execution, and the basis
/// for <see cref="IAuditRecordStore.CreateAsync"/>'s create-only append-only guarantee.
/// </summary>
internal static class AuditRecordDocumentId
{
    /// <summary>Builds the audit-record document id for <paramref name="executionId"/>.</summary>
    internal static string ForExecution(string executionId) => $"{executionId}:audit";
}
