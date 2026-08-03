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
            Change = change,
            ProposedAtUtc = proposedAtUtc,
        };

        Assert.Equal("proposal-1", proposal.ProposalId);
        Assert.Equal(change, proposal.Change);
        Assert.Equal(proposedAtUtc, proposal.ProposedAtUtc);
    }
}
