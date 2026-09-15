using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.Audit;

/// <summary>
/// Document ids and types in <c>governance-audit-records</c> (ADR-PA30). Scopes are snake_case, so
/// none can contain a hyphen and no record id can collide with <c>chain-head:</c> or <c>record-id:</c>.
/// </summary>
internal static class GovernanceAuditDocumentId
{
    /// <summary>The <c>doc_type</c> of a signed record.</summary>
    internal const string RecordDocType = "record";

    /// <summary>The <c>doc_type</c> of a chain head.</summary>
    internal const string HeadDocType = "chain_head";

    /// <summary>The <c>doc_type</c> of a record-id marker.</summary>
    internal const string RecordIdDocType = "record_id";

    /// <summary><c>{scope}:{sequence:D12}</c>: zero-padded, so id order is sequence order.</summary>
    internal static string ForRecord(string scope, long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{scope}:{sequence:D12}");

    /// <summary><c>chain-head:{scope}</c>.</summary>
    internal static string ForHead(string scope) => $"chain-head:{scope}";

    /// <summary><c>record-id:{recordId}</c>: its create-only insert makes an identical concurrent append collide.</summary>
    internal static string ForRecordId(string recordId) => $"record-id:{recordId}";

    /// <summary>Whether a change-feed document is a signed record (heads and markers are not archived).</summary>
    internal static bool IsRecordDocument(JsonObject document) =>
        document["doc_type"]?.GetValue<string>() == RecordDocType;
}

/// <summary>A signed record as stored.</summary>
internal sealed record GovernanceAuditRecordDocument
{
    /// <summary><see cref="GovernanceAuditDocumentId.ForRecord"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary><see cref="GovernanceAuditDocumentId.RecordDocType"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    /// <summary>The signed record.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("record")]
    public required SignedGovernanceAuditRecord Record { get; init; }

    /// <summary>Wraps <paramref name="record"/> under its sequence id.</summary>
    internal static GovernanceAuditRecordDocument FromRecord(SignedGovernanceAuditRecord record) => new()
    {
        Id = GovernanceAuditDocumentId.ForRecord(record.Scope, record.Sequence),
        Scope = record.Scope,
        DocType = GovernanceAuditDocumentId.RecordDocType,
        Record = record,
    };
}

/// <summary>A scope's chain head as stored; replaced only with If-Match on its ETag.</summary>
internal sealed record GovernanceAuditHeadDocument
{
    /// <summary><see cref="GovernanceAuditDocumentId.ForHead"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary><see cref="GovernanceAuditDocumentId.HeadDocType"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    /// <summary>The latest record's sequence.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("sequence")]
    public required long Sequence { get; init; }

    /// <summary>The latest record's hash.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("record_hash")]
    public required string RecordHash { get; init; }

    /// <summary>The head that names <paramref name="record"/>.</summary>
    internal static GovernanceAuditHeadDocument FromRecord(SignedGovernanceAuditRecord record) => new()
    {
        Id = GovernanceAuditDocumentId.ForHead(record.Scope),
        Scope = record.Scope,
        DocType = GovernanceAuditDocumentId.HeadDocType,
        Sequence = record.Sequence,
        RecordHash = record.RecordHash,
    };

    /// <summary>The public head shape.</summary>
    internal GovernanceAuditChainHead ToHead() => new() { Scope = Scope, Sequence = Sequence, RecordHash = RecordHash };
}

/// <summary>Maps a record id to its sequence within a scope; create-only.</summary>
internal sealed record GovernanceAuditRecordIdDocument
{
    /// <summary><see cref="GovernanceAuditDocumentId.ForRecordId"/>.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The partition key.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary><see cref="GovernanceAuditDocumentId.RecordIdDocType"/>.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    /// <summary>The record id.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName("record_id")]
    public required string RecordId { get; init; }

    /// <summary>The record's sequence.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName("sequence")]
    public required long Sequence { get; init; }

    /// <summary>The marker for <paramref name="record"/>.</summary>
    internal static GovernanceAuditRecordIdDocument FromRecord(SignedGovernanceAuditRecord record) => new()
    {
        Id = GovernanceAuditDocumentId.ForRecordId(record.RecordId),
        Scope = record.Scope,
        DocType = GovernanceAuditDocumentId.RecordIdDocType,
        RecordId = record.RecordId,
        Sequence = record.Sequence,
    };
}

/// <summary>Builds the parameterised query for <see cref="IGovernanceAuditService.QueryAsync"/> (ADR-PA30). Pure and unit-tested.</summary>
internal static class GovernanceAuditQueryBuilder
{
    /// <summary>Records only, filtered by every filter <paramref name="query"/> sets, in sequence order.</summary>
    internal static QueryDefinition Build(GovernanceAuditQuery query)
    {
        var clauses = new List<string> { "c.doc_type = @docType" };
        var parameters = new List<(string Name, object Value)> { ("@docType", GovernanceAuditDocumentId.RecordDocType) };

        AddEquals("c.record.subject_type", "@subjectType", query.SubjectType, clauses, parameters);
        AddEquals("c.record.subject_id", "@subjectId", query.SubjectId, clauses, parameters);
        AddEquals("c.record.actor", "@actor", query.Actor, clauses, parameters);
        AddEquals("c.record.event_type", "@eventType", query.EventType, clauses, parameters);
        AddBound("c.record.occurred_at_utc >= @fromUtc", "@fromUtc", query.FromUtc, clauses, parameters);
        AddBound("c.record.occurred_at_utc <= @toUtc", "@toUtc", query.ToUtc, clauses, parameters);

        var definition = new QueryDefinition($"SELECT * FROM c WHERE {string.Join(" AND ", clauses)} ORDER BY c.record.sequence ASC");
        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }

    private static void AddEquals(string path, string name, string? value, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (value is null)
        {
            return;
        }

        clauses.Add($"{path} = {name}");
        parameters.Add((name, value));
    }

    private static void AddBound(string clause, string name, DateTime? value, List<string> clauses, List<(string Name, object Value)> parameters)
    {
        if (value is not { } bound)
        {
            return;
        }

        clauses.Add(clause);
        parameters.Add((name, bound));
    }
}
