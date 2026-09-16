namespace Frontier.Platform.Audit;

/// <summary>
/// Deterministic Cosmos document <c>id</c> formatting for the <c>audit-records</c> container
/// (doc 05 §6): <c>{executionId}:audit</c> — one signed record per execution, and the basis
/// for <see cref="IAuditRecordStore.CreateAsync"/>'s create-only append-only guarantee. ADR-PA31
/// adds one head document per engagement beside the records, in the same partition.
/// </summary>
internal static class AuditRecordDocumentId
{
    /// <summary>The <c>doc_type</c> of a signed record. Records written before ADR-PA31 carry no <c>doc_type</c> at all.</summary>
    internal const string RecordDocType = "record";

    /// <summary>The <c>doc_type</c> of an engagement's chain head.</summary>
    internal const string HeadDocType = "chain_head";

    /// <summary>
    /// The Cosmos SQL predicate selecting record documents only. A record written before ADR-PA31 has
    /// no <c>doc_type</c> property, so the absent case must match — omitting it would hide every
    /// pre-upgrade record from its own chain, which is exactly the kind of silent evidence loss this
    /// package exists to prevent.
    /// </summary>
    internal const string RecordDocumentPredicate = $"(NOT IS_DEFINED(c.doc_type) OR c.doc_type = \"{RecordDocType}\")";

    /// <summary>Builds the audit-record document id for <paramref name="executionId"/>.</summary>
    internal static string ForExecution(string executionId) => $"{executionId}:audit";

    /// <summary>
    /// Builds the chain-head document id for <paramref name="engagementId"/>. An execution id is an
    /// opaque run token (ADR-PA20) and a record id always ends in <c>:audit</c>, so the
    /// <c>chain-head:</c> prefix cannot collide with one.
    /// </summary>
    internal static string ForHead(string engagementId) => $"chain-head:{engagementId}";
}
