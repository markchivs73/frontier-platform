using System.Text;
using System.Text.Json;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 byte-stability, round-trip and isolation tests for the stored role-definition proposal.
/// A stored proposal is a governance record, so a change to these golden files is a compatibility
/// break, not a formatting tweak.
/// </summary>
public sealed class RoleDefinitionProposalDocumentTests
{
    /// <summary>The proposal once approved, carrying every stamp and a decision.</summary>
    internal static RoleDefinitionProposal Approved => RoleDefinitionGovernanceService.ApplyApproval(
        Pending() with { PredecessorDefinitionVersion = 1 },
        AgentDefinition() with { InputContractHash = FakeContractCatalog.HashOf(InputRef), OutputContractHash = FakeContractCatalog.HashOf(OutputRef) },
        2,
        Approver,
        "contracts reviewed",
        FixedTimeProvider.Now.AddHours(1));

    [Fact]
    public void PendingDocument_IsByteStableAndMatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(RoleDefinitionProposalDocument.FromDomain(Pending()));

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_definition_proposal_document_pending.json");
    }

    [Fact]
    public void ApprovedDocument_IsByteStableAndMatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(RoleDefinitionProposalDocument.FromDomain(Approved));

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_definition_proposal_document_approved.json");
    }

    [Fact]
    public void EveryDocument_RoundTripsWithoutChangingBytes()
    {
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(RoleDefinitionProposalDocument.FromDomain(Pending()));
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(RoleDefinitionProposalDocument.FromDomain(Approved));
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(RoleDefinitionProposalDocument.FromDomain(Pending(ModelDefinition())));
    }

    [Fact]
    public void FromDomain_ThenToDomain_RestoresEveryField()
    {
        Assert.Equal(Pending(), RoleDefinitionProposalDocument.FromDomain(Pending()).ToDomain());
        Assert.Equal(Approved, RoleDefinitionProposalDocument.FromDomain(Approved).ToDomain());
        Assert.Equal(Pending(ModelDefinition()), RoleDefinitionProposalDocument.FromDomain(Pending(ModelDefinition())).ToDomain());
    }

    [Fact]
    public void FromDomain_SetsTheDeterministicIdAndDocType_AndNeverAMappingVersion()
    {
        var document = RoleDefinitionProposalDocument.FromDomain(Approved);
        using var json = JsonDocument.Parse(CanonicalProfile.SerializeCanonical(document));

        Assert.Equal("gen-record-pass:definition-proposal:d-1", document.Id);
        Assert.Equal("role_definition_proposal", document.DocType);
        Assert.Equal(-1, document.Ttl);
        // The mapping-version queries select on the {role}:v prefix and on mapping_version (ADR-PA40).
        Assert.False(document.Id.StartsWith(ModelRoleConfigDocumentId.VersionPrefix(AgentRoleId), StringComparison.Ordinal));
        Assert.False(json.RootElement.TryGetProperty("mapping_version", out _));
        Assert.False(json.RootElement.TryGetProperty("_etag", out _));
    }

    [Fact]
    public void EveryStoredKey_IsSnakeCase()
    {
        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(RoleDefinitionProposalDocument.FromDomain(Approved)));

        Assert.DoesNotMatch("\"[^\"]*[A-Z][^\"]*\":", json);
    }

    [Fact]
    public void ToDomain_StoredIllShapedDefinition_IsRefusedOnRead()
    {
        var edited = RoleDefinitionProposalDocument.FromDomain(Pending()) with { InputContractRef = null };

        Assert.Throws<ContractViolationException>(edited.ToDomain);
    }

    [Fact]
    public void Proposal_IsAVersionedContract_WhoseValidateEnforcesTheRules()
    {
        var contract = Pending();

        Assert.IsAssignableFrom<IVersionedContract>(contract);
        Assert.Equal("1.0", contract.SchemaVersion);
        contract.Validate();
        Assert.Throws<InvalidRoleDefinitionException>(() => (Pending() with { ProposedBy = " " }).Validate());
    }
}
