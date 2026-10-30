using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 canonical-serialization tests for the public <see cref="RoleDefinitionProposal"/>
/// contract, as a consumer returns it from doc 20's definition-proposal routes: round-trip,
/// byte-stability across cultures, and golden files. The nested <see cref="RoleDefinition"/> and
/// <see cref="CapabilityRequirements"/> are pinned inline here, snake_case and ordered.
/// </summary>
public sealed class RoleDefinitionProposalTests
{
    [Fact]
    public void PendingAgentProposal_IsStable_MatchesItsGoldenFile_AndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Pending(), "role_definition_proposal_pending.json");

    [Fact]
    public void ApprovedAgentProposal_IsStable_MatchesItsGoldenFile_AndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(
            RoleDefinitionProposalDocumentTests.Approved with { ConcurrencyToken = "\"etag-2\"" },
            "role_definition_proposal_approved.json");

    [Fact]
    public void PendingModelProposal_IsStable_MatchesItsGoldenFile_AndRoundTrips() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Pending(ModelDefinition()), "role_definition_proposal_model.json");

    [Fact]
    public void Deserializing_RestoresEveryField()
    {
        var approved = RoleDefinitionProposalDocumentTests.Approved;

        Assert.Equal(approved, RoundTrip(approved));
        Assert.Equal(Pending(ModelDefinition()), RoundTrip(Pending(ModelDefinition())));
    }

    [Fact]
    public void EveryKey_IsSnakeCase_IncludingTheNestedDefinition()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(Pending(ModelDefinition())));

        Assert.DoesNotMatch("\"[^\"]*[A-Z][^\"]*\":", json);
        Assert.Contains("\"proposed_definition\":{\"role_id\":", json, StringComparison.Ordinal);
        Assert.Contains("\"requirements\":{\"min_context_window\":", json, StringComparison.Ordinal);
    }

    private static RoleDefinitionProposal RoundTrip(RoleDefinitionProposal proposal) =>
        JsonSerializer.Deserialize<RoleDefinitionProposal>(CanonicalProfile.SerializeCanonical(proposal), CanonicalProfile.Options)!;
}
