using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.Audit;

/// <summary>
/// Records governance changes made outside an execution as signed, chained records, and reads
/// and verifies them (ADR-PA30). A consumer calls <see cref="AppendAsync"/> once per mutation.
/// </summary>
public interface IGovernanceAuditService
{
    /// <summary>
    /// Signs and stores <paramref name="entry"/> as the next record in its scope, returning it. Fails
    /// closed: it returns the stored record or throws. An identical entry already stored is returned
    /// unchanged.
    /// </summary>
    /// <exception cref="Frontier.Platform.Abstractions.ContractViolationException">The entry is invalid, or it compensates a record that does not exist. Permanent; never retry.</exception>
    /// <exception cref="GovernanceAuditAppendException">The append did not land after the configured concurrency retries, or the store refused it.</exception>
    Task<SignedGovernanceAuditRecord> AppendAsync(GovernanceAuditEntry entry, CancellationToken cancellationToken);

    /// <summary>The record with <paramref name="recordId"/>, or <see langword="null"/>.</summary>
    Task<SignedGovernanceAuditRecord?> GetAsync(string recordId, CancellationToken cancellationToken);

    /// <summary>One page of a scope's records matching <paramref name="query"/>, in sequence order.</summary>
    Task<GovernanceAuditPage> QueryAsync(GovernanceAuditQuery query, CancellationToken cancellationToken);

    /// <summary>Verifies <paramref name="scope"/>'s whole chain and head. Reports breaks rather than throwing.</summary>
    Task<GovernanceAuditVerificationResult> VerifyAsync(string scope, CancellationToken cancellationToken);
}

/// <summary>Filters and paging for <see cref="IGovernanceAuditService.QueryAsync"/>. Every filter is optional.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain query record; exercised by GovernanceAuditQueryBuilder tests.")]
public sealed record GovernanceAuditQuery
{
    /// <summary>The largest page a query may ask for.</summary>
    public const int MaxPageSize = 500;

    /// <summary>The scope to read.</summary>
    public string Scope { get; init; } = GovernanceAuditScopes.Deployment;

    /// <summary>Only records about this subject type.</summary>
    public string? SubjectType { get; init; }

    /// <summary>Only records about this subject id.</summary>
    public string? SubjectId { get; init; }

    /// <summary>Only records by this actor.</summary>
    public string? Actor { get; init; }

    /// <summary>Only records of this event type.</summary>
    public string? EventType { get; init; }

    /// <summary>Only records that occurred at or after this instant.</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>Only records that occurred at or before this instant.</summary>
    public DateTime? ToUtc { get; init; }

    /// <summary>Records per page, 1 to <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>The token from the previous page, or <see langword="null"/> for the first.</summary>
    public string? ContinuationToken { get; init; }
}

/// <summary>A page of governance records.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract.")]
public sealed record GovernanceAuditPage
{
    /// <summary>The records, in sequence order.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("records")]
    public required IReadOnlyList<SignedGovernanceAuditRecord> Records { get; init; }

    /// <summary>Pass back to read the next page; <see langword="null"/> on the last page.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("continuation_token")]
    public string? ContinuationToken { get; init; }
}

/// <summary>A governance audit append did not land (ADR-PA30). A consumer maps it to 503, and the mutation it guards does not proceed.</summary>
public sealed class GovernanceAuditAppendException : Exception
{
    /// <summary>Creates an empty exception (CA1032).</summary>
    public GovernanceAuditAppendException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public GovernanceAuditAppendException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public GovernanceAuditAppendException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
