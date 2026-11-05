using Microsoft.Azure.Cosmos;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The lookups the <c>model-role-config</c> stores run inside a role's partition (ADR-PA32, ADR-PA40): which
/// proposal states count as undecided, and which proposal a live version came from. Kept apart from the
/// coverage-excluded <see cref="CosmosMappingProposalStore"/> and <see cref="CosmosRoleDefinitionStore"/>
/// (S13.193) so the unit gate measures the selection.
/// </summary>
internal static class ModelRoleConfigQueries
{
    /// <summary>The proposals of <paramref name="docType"/> still awaiting a decision (every state whose <c>IsAwaitingDecision</c> holds).</summary>
    internal static QueryDefinition UndecidedProposals(string docType) =>
        new QueryDefinition("SELECT * FROM c WHERE c.doc_type = @docType AND ARRAY_CONTAINS(@states, c.state)")
            .WithParameter("@docType", docType)
            .WithParameter("@states", MappingProposalState.List.Where(state => state.IsAwaitingDecision).Select(state => state.Name).ToArray());

    /// <summary>
    /// The mapping proposal that made <paramref name="version"/> live: the one that promoted it, or — for a proposal
    /// never promoted — the one that allocated it.
    /// </summary>
    internal static QueryDefinition MappingProposalByLiveVersion(int version) =>
        new QueryDefinition(
            "SELECT * FROM c WHERE c.doc_type = @docType AND (c.promoted_version = @version OR (NOT IS_DEFINED(c.promoted_version) AND c.mapping_version = @version))")
            .WithParameter("@docType", MappingProposalDocument.ProposalDocType)
            .WithParameter("@version", version);
}
