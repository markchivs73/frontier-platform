namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// <see cref="ModelRoleConfigQueries"/> — the in-partition lookups the <c>model-role-config</c> stores run
/// (ADR-PA32, ADR-PA40). They sat inside the coverage-excluded stores until S13.193.
/// </summary>
public sealed class ModelRoleConfigQueriesTests
{
    [Theory]
    [InlineData(MappingProposalDocument.ProposalDocType)]
    [InlineData(RoleDefinitionProposalDocument.ProposalDocType)]
    public void UndecidedProposals_SelectsTheDocTypeInEveryAwaitingState(string docType)
    {
        var definition = ModelRoleConfigQueries.UndecidedProposals(docType);

        Assert.Equal("SELECT * FROM c WHERE c.doc_type = @docType AND ARRAY_CONTAINS(@states, c.state)", definition.QueryText);
        var parameters = definition.GetQueryParameters().ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(docType, parameters["@docType"]);
        var expected = MappingProposalState.List.Where(state => state.IsAwaitingDecision).Select(state => state.Name).ToArray();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, (string[])parameters["@states"]);
    }

    [Fact]
    public void MappingProposalByLiveVersion_MatchesThePromotedVersionOrAnUnpromotedAllocation()
    {
        var definition = ModelRoleConfigQueries.MappingProposalByLiveVersion(7);

        Assert.Contains("c.doc_type = @docType", definition.QueryText, StringComparison.Ordinal);
        Assert.Contains("c.promoted_version = @version OR (NOT IS_DEFINED(c.promoted_version) AND c.mapping_version = @version)", definition.QueryText, StringComparison.Ordinal);
        var parameters = definition.GetQueryParameters().ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(MappingProposalDocument.ProposalDocType, parameters["@docType"]);
        Assert.Equal(7, parameters["@version"]);
    }
}
