using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Filters and paging for <see cref="IMappingGovernanceService.ListProposalsAsync"/> (ADR-PA32,
/// relaxed by ADR-PA34).
/// <para>
/// <see cref="RoleId"/> is <b>optional</b>: D3's headline view is "every proposal awaiting a
/// decision, across every role", which no per-role query can answer. Supplying it keeps the query
/// inside the role's partition; omitting it runs cross-partition, bounded by
/// <see cref="PageSize"/> and paged by <see cref="ContinuationToken"/>.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain query record; its filters are exercised through MappingProposalQueryBuilder and the store tests.")]
public sealed record MappingProposalQuery
{
    /// <summary>The largest page a query may ask for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// The role whose proposals to list, or <see langword="null"/> for every role. When supplied it
    /// is the partition key, and the query is single-partition.
    /// </summary>
    public string? RoleId { get; init; }

    /// <summary>
    /// Only proposals in one of these states; <see langword="null"/> or empty means every state.
    /// <para>
    /// <b>There is deliberately no default</b> (ADR-PA34). D3 defaults its own filter to
    /// <c>pending_approval</c>, but a store that quietly applied that default would make "show me
    /// everything" unexpressible and would silently hide decided proposals from any caller that
    /// forgot the filter. The caller chooses.
    /// </para>
    /// </summary>
    public IReadOnlyList<MappingProposalState>? States { get; init; }

    /// <summary>Proposals per page, 1 to <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>The token from the previous page, or <see langword="null"/> for the first.</summary>
    public string? ContinuationToken { get; init; }
}

/// <summary>A page of proposals, newest first.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data contract.")]
public sealed record MappingProposalPage
{
    /// <summary>The proposals, most recently proposed first.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName("proposals")]
    public required IReadOnlyList<MappingChangeProposal> Proposals { get; init; }

    /// <summary>Pass back to read the next page; <see langword="null"/> on the last page.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName("continuation_token")]
    public string? ContinuationToken { get; init; }
}
