using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Filters and paging for <see cref="IMappingGovernanceService.ListProposalsAsync"/> (ADR-PA32).
/// <see cref="RoleId"/> is required: proposals live in the role's own partition, so a query without
/// one would fan out across every role for no governance purpose.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain query record; its filters are exercised through MappingProposalQueryBuilder and the store tests.")]
public sealed record MappingProposalQuery
{
    /// <summary>The largest page a query may ask for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The role whose proposals to list. Required — it is the partition key.</summary>
    public required string RoleId { get; init; }

    /// <summary>Only proposals in this state.</summary>
    public MappingProposalState? State { get; init; }

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
