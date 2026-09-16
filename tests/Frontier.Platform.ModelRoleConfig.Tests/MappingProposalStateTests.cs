namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 tests for <see cref="MappingProposalState"/>'s transition table (ADR-PA32, doc 08 §7).
/// The legal moves and the refused ones are pinned together: a state machine that only proves what
/// it allows has not been tested.
/// </summary>
public sealed class MappingProposalStateTests
{
    [Theory]
    [InlineData("pending_approval", "shadow")]
    [InlineData("pending_approval", "approved")]
    [InlineData("pending_approval", "rejected")]
    [InlineData("pending_approval", "withdrawn")]
    [InlineData("shadow", "approved")]
    [InlineData("shadow", "rejected")]
    [InlineData("shadow", "withdrawn")]
    [InlineData("approved", "promoted")]
    [InlineData("approved", "rolled_back")]
    [InlineData("promoted", "rolled_back")]
    public void CanTransitionTo_LegalMove_IsAllowed(string from, string to) =>
        Assert.True(MappingProposalState.FromName(from).CanTransitionTo(MappingProposalState.FromName(to)));

    [Theory]
    [InlineData("pending_approval", "promoted")]
    [InlineData("pending_approval", "rolled_back")]
    [InlineData("pending_approval", "pending_approval")]
    [InlineData("approved", "approved")]
    [InlineData("approved", "rejected")]
    [InlineData("approved", "withdrawn")]
    [InlineData("promoted", "approved")]
    [InlineData("promoted", "promoted")]
    [InlineData("shadow", "promoted")]
    [InlineData("rejected", "approved")]
    [InlineData("withdrawn", "approved")]
    [InlineData("rolled_back", "approved")]
    public void CanTransitionTo_IllegalMove_IsRefused(string from, string to) =>
        Assert.False(MappingProposalState.FromName(from).CanTransitionTo(MappingProposalState.FromName(to)));

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("rolled_back")]
    public void IsTerminal_TerminalStates_AreTerminal(string name) =>
        Assert.True(MappingProposalState.FromName(name).IsTerminal);

    [Theory]
    [InlineData("pending_approval")]
    [InlineData("shadow")]
    [InlineData("approved")]
    [InlineData("promoted")]
    public void IsTerminal_LiveStates_AreNotTerminal(string name) =>
        Assert.False(MappingProposalState.FromName(name).IsTerminal);

    [Fact]
    public void CanTransitionTo_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => MappingProposalState.PendingApproval.CanTransitionTo(null!));

    [Fact]
    public void List_CarriesTheShadowSlotForLater()
    {
        // S13.101: shadow execution is sequenced after the cloud proof, but the slot ships now so
        // enabling it later is an additive transition rather than a rename of a shipped value.
        Assert.Contains(MappingProposalState.Shadow, MappingProposalState.List);
        Assert.Equal(7, MappingProposalState.List.Count);
    }

    [Fact]
    public void AwaitingDecisionAndReleasingStates_PartitionEveryDeclaredValue()
    {
        // The two sets are named explicitly, never derived from IsTerminal. This pins that every
        // declared state belongs to exactly one of them, so adding a value forces the choice.
        Assert.All(MappingProposalState.List, state =>
            Assert.True(state.IsAwaitingDecision ^ state.ReleasesRoleProposalSlot, $"{state.Name} must be in exactly one set"));
    }

    [Theory]
    [InlineData("pending_approval")]
    [InlineData("shadow")]
    public void IsAwaitingDecision_HoldsTheRolesProposalSlot(string name) =>
        Assert.True(MappingProposalState.FromName(name).IsAwaitingDecision);

    [Theory]
    [InlineData("approved")]
    [InlineData("promoted")]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("rolled_back")]
    public void ReleasesRoleProposalSlot_IncludesTheDecidedButNonTerminalStates(string name)
    {
        // approved and promoted are non-terminal, yet must release the slot: a role promoted to fleet
        // obviously still accepts the next proposal.
        var state = MappingProposalState.FromName(name);

        Assert.True(state.ReleasesRoleProposalSlot);
        Assert.False(state.IsAwaitingDecision);
    }

    [Fact]
    public void ReleasesRoleProposalSlot_IsNotTheSameQuestionAsTerminality()
    {
        Assert.False(MappingProposalState.Approved.IsTerminal);
        Assert.True(MappingProposalState.Approved.ReleasesRoleProposalSlot);
        Assert.False(MappingProposalState.Promoted.IsTerminal);
        Assert.True(MappingProposalState.Promoted.ReleasesRoleProposalSlot);
    }

    [Fact]
    public void FromName_UnknownWireValue_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MappingProposalState.FromName("approved_ish"));
}
