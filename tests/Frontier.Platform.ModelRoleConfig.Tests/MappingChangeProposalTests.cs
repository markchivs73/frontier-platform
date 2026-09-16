namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S4.3 tests for <see cref="MappingChangeProposal"/> (doc 08 §7).</summary>
public sealed class MappingChangeProposalTests
{
    [Fact]
    public void Properties_RoundTripThroughInitializer()
    {
        var change = new MappingChange
        {
            RoleId = "deep-reasoning",
            ProposedMapping = Phase1RoleCatalogue.DeepReasoningMappingV1,
            Reason = "test",
        };
        var proposedAtUtc = new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc);

        var proposal = new MappingChangeProposal
        {
            ProposalId = "proposal-1",
            RoleId = "deep-reasoning",
            State = MappingProposalState.PendingApproval,
            ProposedBy = "user:oid-proposer",
            Change = change,
            ProposedAtUtc = proposedAtUtc,
        };

        Assert.Equal("proposal-1", proposal.ProposalId);
        Assert.Equal(change, proposal.Change);
        Assert.Equal(proposedAtUtc, proposal.ProposedAtUtc);
        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Equal("1.0", proposal.SchemaVersion);
    }

    [Fact]
    public void ADecidedProposalCarriesItsAttribution()
    {
        // ADR-PA32: the service stamps these; they are never taken from the caller.
        var approved = MappingProposalSamples.Pending() with
        {
            State = MappingProposalState.Approved,
            MappingVersion = 2,
            ApprovedBy = "user:oid-approver",
            EffectiveFromUtc = FixedTimeProvider.Now,
        };

        approved.Validate();

        Assert.Equal(2, approved.MappingVersion);
        Assert.Equal("user:oid-approver", approved.ApprovedBy);
    }
}
