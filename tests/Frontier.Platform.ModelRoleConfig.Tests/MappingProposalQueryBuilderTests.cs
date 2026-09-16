using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 tests for <see cref="MappingProposalQueryBuilder"/> (ADR-PA32). The builder is pure, so
/// the query's shape is pinned here rather than behind an emulator: it must select proposal documents
/// only — versions and the <c>current</c> pointer share the partition — and order newest first.
/// </summary>
public sealed class MappingProposalQueryBuilderTests
{
    [Fact]
    public void Build_WithoutAStateFilter_SelectsProposalDocumentsOnly()
    {
        var definition = MappingProposalQueryBuilder.Build(new MappingProposalQuery { RoleId = RoleId });

        Assert.Contains("c.doc_type = @docType", definition.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain("c.proposal.state", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains("ORDER BY c.proposal.proposed_at_utc DESC", definition.QueryText, StringComparison.Ordinal);
        var parameters = definition.GetQueryParameters();
        Assert.Equal(("@docType", (object)MappingProposalDocument.ProposalDocType), Assert.Single(parameters));
    }

    [Fact]
    public void Build_WithAStateFilter_AddsItAsAParameter()
    {
        var query = new MappingProposalQuery { RoleId = RoleId, State = MappingProposalState.PendingApproval };

        var definition = MappingProposalQueryBuilder.Build(query);

        Assert.Contains("c.proposal.state = @state", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains(definition.GetQueryParameters(), parameter => parameter.Name == "@state" && (string)parameter.Value == "pending_approval");
    }

    [Fact]
    public void VersionPrefix_MatchesVersionDocumentsAndNothingElseInThePartition()
    {
        // The discriminator the mapping-version listing selects on. It replaced "has no doc_type",
        // which would have silently returned an empty list - and so reallocated from v1 - the day
        // version documents gained one. These three ids share the role's partition.
        var prefix = ModelRoleConfigDocumentId.VersionPrefix(RoleId);

        Assert.Equal("deep-reasoning:v", prefix);
        Assert.StartsWith(prefix, ModelRoleConfigDocumentId.ForVersion(RoleId, 7), StringComparison.Ordinal);
        Assert.False(ModelRoleConfigDocumentId.ForCurrent(RoleId).StartsWith(prefix, StringComparison.Ordinal));
        Assert.False(ModelRoleConfigDocumentId.ForProposal(RoleId, "abc123").StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void IsConcurrencyConflict_ClassifiesOnlyTheTwoThatMeanAnotherWriterWon()
    {
        Assert.True(CosmosMappingProposalStore.IsConcurrencyConflict(System.Net.HttpStatusCode.PreconditionFailed));
        Assert.True(CosmosMappingProposalStore.IsConcurrencyConflict(System.Net.HttpStatusCode.Conflict));
        Assert.False(CosmosMappingProposalStore.IsConcurrencyConflict(System.Net.HttpStatusCode.NotFound));
        Assert.False(CosmosMappingProposalStore.IsConcurrencyConflict(System.Net.HttpStatusCode.RequestEntityTooLarge));
    }
}

/// <summary>S13.101 tests for <see cref="MappingGovernanceException"/>, the "a legal decision did not land" signal (ADR-PA32).</summary>
public sealed class MappingGovernanceExceptionTests
{
    [Fact]
    public void Constructors_CarryTheirMessageAndInnerException()
    {
        var inner = new InvalidOperationException("batch failed");

        Assert.NotNull(new MappingGovernanceException().Message);
        Assert.Equal("allocation lost", new MappingGovernanceException("allocation lost").Message);

        var wrapped = new MappingGovernanceException("allocation lost", inner);
        Assert.Equal("allocation lost", wrapped.Message);
        Assert.Same(inner, wrapped.InnerException);
    }
}
