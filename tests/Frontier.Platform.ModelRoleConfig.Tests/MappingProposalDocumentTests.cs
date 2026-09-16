using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.TestSupport;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 byte-stability and round-trip suite for the proposal document (ADR-PA32). The stored
/// bytes are the governance evidence of what was proposed and who decided it, so a change to this
/// golden file is a compatibility break, not a formatting tweak.
/// </summary>
public sealed class MappingProposalDocumentTests
{
    [Fact]
    public void MappingProposalDocument_IsByteStableAndRoundTrips() =>
        ContractRoundTripAssertions.AssertByteStableAndRoundTrips(MappingProposalDocument.FromDomain(Pending()));

    [Fact]
    public void MappingProposalDocument_MatchesItsGoldenFile()
    {
        var bytes = ContractRoundTripAssertions.AssertByteStableAcrossCultures(MappingProposalDocument.FromDomain(Pending()));

        ContractRoundTripAssertions.AssertMatchesGoldenFile(bytes, "mapping_proposal_document.json");
    }

    [Fact]
    public void FromDomain_SetsTheDeterministicIdAndPartitionKey()
    {
        var document = MappingProposalDocument.FromDomain(Pending());

        Assert.Equal("deep-reasoning:proposal:p-1", document.Id);
        Assert.Equal(RoleId, document.RoleId);
        Assert.Equal(MappingProposalDocument.ProposalDocType, document.DocType);
        Assert.Equal(-1, document.Ttl);
    }

    [Fact]
    public void EveryStoredKeyIsSnakeCase()
    {
        // Invariant 3 / K10: the document owns its own flat shape precisely so the domain records'
        // attribute-free PascalCase can never reach stored bytes.
        var json = CanonicalProfile.SerializeCanonical(MappingProposalDocument.FromDomain(Pending()));

        using var document = JsonDocument.Parse(json);
        Assert.All(document.RootElement.EnumerateObject(), property =>
            Assert.DoesNotMatch("[A-Z]", property.Name));
    }

    [Fact]
    public void FromDomain_StoresTheChainExactlyOnce()
    {
        var document = MappingProposalDocument.FromDomain(Pending());

        Assert.Single(document.Chain);
        Assert.Equal("claude-fable-5", document.Chain[0].ModelId);
    }

    [Fact]
    public void FromDomain_ThenToDomain_RestoresTheChain()
    {
        var proposal = Pending();

        var roundTripped = MappingProposalDocument.FromDomain(proposal).ToDomain();

        Assert.Equal(proposal.Change.ProposedMapping.Chain, roundTripped.Change.ProposedMapping.Chain);
        Assert.Equal(proposal.ProposalId, roundTripped.ProposalId);
        Assert.Equal(proposal.State, roundTripped.State);
    }

    [Fact]
    public void ToDomain_StoredMixedChain_IsRefusedOnRead()
    {
        // ADR-PA27: a proposal edited outside this library still cannot yield a mixed chain.
        var document = MappingProposalDocument.FromDomain(Pending());
        var agent = ChainEntryDocument.FromAgent(new AgentEntry
        {
            Provider = AgentEntry.A2aProvider,
            Currency = "USD",
            ResourceName = "com.azure.foundry/echo",
            ResourceVersion = "1.0",
            CostPerInvocation = 0.02m,
        });
        var mixed = document with { Chain = [.. document.Chain, agent] };

        Assert.Throws<Frontier.Platform.Abstractions.ContractViolationException>(() => mixed.ToDomain());
    }

    [Fact]
    public void ADocumentWithoutAnyDecisionMembers_ReadsBackWithThemNull()
    {
        // ADR-E15's compatibility floor: every member beyond a pending proposal's own is optional and
        // omitted when null, so a document stored before a later decision member existed still reads.
        const string stored = """
            {"id":"deep-reasoning:proposal:p-old","role_id":"deep-reasoning","doc_type":"mapping_proposal",
             "proposal_id":"p-old","state":"pending_approval","proposed_by":"user:x",
             "proposed_at_utc":"2026-09-16T12:00:00.000Z","change_reason":"r","ring":"canary","canary_percent":10,
             "chain":[{"provider":"anthropic","model_id":"claude-fable-5","input_cost_per_1k":"0.0100",
             "output_cost_per_1k":"0.0500","cache_read_cost_per_1k":"0.0010","currency":"USD",
             "context_window":200000,"max_output_tokens":16000}],"ttl":-1}
            """;

        var proposal = JsonSerializer.Deserialize<MappingProposalDocument>(stored, CanonicalProfile.Options)!.ToDomain();

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Null(proposal.MappingVersion);
        Assert.Null(proposal.ApprovedBy);
        Assert.Null(proposal.PromotedVersion);
        Assert.Null(proposal.RolledBackToVersion);
        Assert.Null(proposal.PredecessorFleetVersion);
        Assert.Null(proposal.DecidedBy);
        Assert.Null(proposal.DecidedAtUtc);
        Assert.Null(proposal.DecisionReason);
    }
}
