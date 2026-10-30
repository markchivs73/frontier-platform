using System.Text;
using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using static Frontier.Platform.ModelRoleConfig.Tests.EvaluationEvidenceSamples;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 byte-stability tests for the stamped contract hashes on the mapping version and proposal
/// documents (ADR-E27 D16). The new members are optional, so a document written before v0.45.0 keeps
/// its bytes and still reads (the ADR-E15 additive floor): both shapes are pinned by golden files.
/// </summary>
public sealed class EvaluatedMappingDocumentTests
{
    [Fact]
    public void RoleMappingDocument_WithoutHashes_MatchesTheLegacyGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(RoleMappingDocument.FromDomain(Phase1RoleCatalogue.DeepReasoningMappingV1));

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_mapping_document.json");
    }

    [Fact]
    public void RoleMappingDocument_WithEvidenceAndHashes_MatchesItsGoldenFileAndRoundTrips()
    {
        var document = RoleMappingDocument.FromDomain(EvaluatedVersion());

        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(document);

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "role_mapping_document_evaluated.json");
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(document);
    }

    [Fact]
    public void RoleMappingDocument_LegacyBytes_ReadWithNoHashes()
    {
        var mapping = Read<RoleMappingDocument>("role_mapping_document.json").ToDomain();

        Assert.Null(mapping.InputContractHash);
        Assert.Null(mapping.OutputContractHash);
    }

    [Fact]
    public void RoleMappingDocument_EvaluatedBytes_ReadBackTheHashes()
    {
        var mapping = Read<RoleMappingDocument>("role_mapping_document_evaluated.json").ToDomain();

        Assert.Equal(AgentRole.InputContractHash, mapping.InputContractHash);
        Assert.Equal(AgentRole.OutputContractHash, mapping.OutputContractHash);
        Assert.Equal(ManualRef, mapping.EvaluationEvidenceRef);
    }

    [Fact]
    public void MappingProposalDocument_WithEvidenceAndHashes_MatchesItsGoldenFileAndRoundTrips()
    {
        var document = MappingProposalDocument.FromDomain(EvaluatedProposal());

        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(document);

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "mapping_proposal_document_evaluated.json");
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(document);
    }

    [Fact]
    public void MappingProposalDocument_EvaluatedBytes_ReadBackTheHashes()
    {
        var mapping = Read<MappingProposalDocument>("mapping_proposal_document_evaluated.json").ToDomain().Change.ProposedMapping;

        Assert.Equal(AgentRole.InputContractHash, mapping.InputContractHash);
        Assert.Equal(AgentRole.OutputContractHash, mapping.OutputContractHash);
    }

    [Fact]
    public void MappingProposalDocument_LegacyBytes_ReadWithNoHashes()
    {
        var mapping = Read<MappingProposalDocument>("mapping_proposal_document.json").ToDomain().Change.ProposedMapping;

        Assert.Null(mapping.InputContractHash);
        Assert.Null(mapping.OutputContractHash);
    }

    [Fact]
    public void BothDocuments_NameTheHashesInSnakeCase()
    {
        var version = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(RoleMappingDocument.FromDomain(EvaluatedVersion())));
        var proposal = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(MappingProposalDocument.FromDomain(EvaluatedProposal())));

        Assert.All(new[] { version, proposal }, json =>
        {
            Assert.Contains("\"input_contract_hash\":", json, StringComparison.Ordinal);
            Assert.Contains("\"output_contract_hash\":", json, StringComparison.Ordinal);
        });
    }

    private static RoleMapping EvaluatedVersion() => MappingEligibilityRules.Stamp(AgentMapping(), AgentRole) with
    {
        MappingVersion = 1,
        ApprovedBy = Approver,
        PredecessorFleetVersion = null,
    };

    private static MappingChangeProposal EvaluatedProposal() => new()
    {
        ProposalId = "p-agent",
        RoleId = AgentRole.RoleId,
        State = MappingProposalState.PendingApproval,
        ProposedBy = Proposer,
        ProposedAtUtc = FixedTimeProvider.Now,
        Change = AgentChange() with { ProposedMapping = MappingEligibilityRules.Stamp(AgentMapping(), AgentRole) },
    };

    private static T Read<T>(string goldenFileName) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "GoldenFiles", goldenFileName)), CanonicalProfile.Options)!;
}
