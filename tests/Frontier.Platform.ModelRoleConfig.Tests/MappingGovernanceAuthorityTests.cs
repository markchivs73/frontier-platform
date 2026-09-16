using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 tests for the governance rules Mark settled on 2026-09-16 (ADR-PA32): one undecided
/// proposal per role, proposer-only withdrawal, distinct-approver rejection, and a canary percentage
/// that cannot silently serve nobody.
/// </summary>
public sealed class MappingGovernanceAuthorityTests
{
    private const string ThirdParty = "user:oid-someone-else";

    [Fact]
    public async Task ProposeAsync_WhileAnotherIsPending_IsRefusedAndNamesIt()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, recorder).ProposeAsync(Change(), ThirdParty, CancellationToken.None));

        Assert.Contains("p-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Proposer, exception.Message, StringComparison.Ordinal);
        // A refusal writes nothing: the audit record is appended only once the change is going ahead.
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_WhileOneIsInShadow_IsRefused()
    {
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1, current: true)
            .WithProposal(Pending() with { State = MappingProposalState.Shadow });

        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), ThirdParty, CancellationToken.None));
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("approved")]
    [InlineData("promoted")]
    [InlineData("rolled_back")]
    public async Task ProposeAsync_OnceTheFirstIsDecided_IsAllowed(string decidedState)
    {
        // The slot is held by a proposal awaiting a decision, not by a non-terminal one: approved and
        // promoted can still be rolled back, but they have been decided, so they release it.
        var decided = Pending() with
        {
            State = MappingProposalState.FromName(decidedState),
            MappingVersion = 2,
            ApprovedBy = Approver,
            PromotedVersion = decidedState == "promoted" ? 3 : null,
            RolledBackToVersion = decidedState == "rolled_back" ? 2 : null,
        };
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(decided);

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), ThirdParty, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
    }

    [Fact]
    public async Task ProposeAsync_WhileAnEarlierCanaryIsLive_IsAllowed()
    {
        // approved is non-terminal — a rollback could still move it — but the canary is live and
        // decided, so the role must still accept the next proposal.
        var canary = FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 };
        var live = Pending() with { State = MappingProposalState.Approved, MappingVersion = 2, ApprovedBy = Approver };
        var store = new FakeMappingProposalStore().WithVersion(FleetV1).WithVersion(canary, current: true).WithProposal(live);

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), ThirdParty, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        // current is the canary v2, whose own fleet predecessor is v1 — so that is what an engagement
        // outside the new canary would keep being served, and what the new proposal records.
        Assert.Equal(1, proposal.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_AfterAPromotion_IsAllowed()
    {
        var fleet = FleetV1 with { MappingVersion = 3, Ring = RolloutRing.Fleet };
        var promoted = Pending() with { State = MappingProposalState.Promoted, MappingVersion = 2, ApprovedBy = Approver, PromotedVersion = 3 };
        var store = new FakeMappingProposalStore().WithVersion(FleetV1).WithVersion(fleet, current: true).WithProposal(promoted);

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), ThirdParty, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Equal(3, proposal.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_FirstProposalForARole_IsAllowed()
    {
        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
            .ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
    }

    [Fact]
    public async Task WithdrawAsync_ByTheProposer_IsAllowed()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var withdrawn = await Service(store, new FakeMappingDecisionRecorder())
            .WithdrawAsync(RoleId, "p-1", Proposer, "superseded", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Withdrawn, withdrawn.State);
    }

    [Fact]
    public async Task WithdrawAsync_ByAnyoneElse_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).WithdrawAsync(RoleId, "p-1", Approver, "not mine to retract", null, CancellationToken.None));

        Assert.Contains("only they may withdraw it", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MappingProposalState.PendingApproval, store.Proposal("p-1")!.State);
    }

    [Fact]
    public async Task RejectAsync_ByTheProposer_IsRefused()
    {
        // A rejection is a recorded decision on someone else's change; killing your own idea is a withdrawal.
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).RejectAsync(RoleId, "p-1", Proposer, "changed my mind", null, CancellationToken.None));

        Assert.Contains("withdraw it instead", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectAsync_ByADistinctPrincipal_IsAllowed()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var rejected = await Service(store, new FakeMappingDecisionRecorder())
            .RejectAsync(RoleId, "p-1", Approver, "no evidence", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
    }

    [Fact]
    public async Task ProposeAsync_CanaryPercentZero_IsRefused()
    {
        // The path a consumer hits by omitting canary_percent from its JSON: int binds as 0, which
        // would approve a mapping that reports itself live in the canary ring while serving nobody.
        var change = Change() with { ProposedMapping = Change().ProposedMapping with { CanaryPercent = 0 } };

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).ProposeAsync(change, Proposer, CancellationToken.None));

        Assert.Contains("canary_percent", exception.Message, StringComparison.Ordinal);
        Assert.Contains("serving no engagement", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_CanaryPercentOneHundred_IsAllowed()
    {
        var change = Change() with { ProposedMapping = Change().ProposedMapping with { CanaryPercent = 100 } };

        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
            .ProposeAsync(change, Proposer, CancellationToken.None);

        Assert.Equal(100, proposal.Change.ProposedMapping.CanaryPercent);
    }

    [Fact]
    public async Task DecisionAttempts_ComeFromOptions()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();
        store.BeforeAllocate = () => store.CreateProposalAsync(Pending(), CancellationToken.None);
        var options = new MappingGovernanceOptions { DecisionMaxAttempts = 3, DecisionBaseDelayMs = 0, DecisionMaxDelayMs = 0 };

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            Service(store, recorder, options: options).ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None));

        Assert.Equal(3, recorder.Compensations.Count);
    }
}

/// <summary>S13.101 tests for <see cref="MappingGovernanceOptions"/> and its backoff curve (ADR-PA32).</summary>
public sealed class MappingGovernanceOptionsTests
{
    [Fact]
    public void Defaults_MatchTheGovernanceAuditPrecedent()
    {
        var options = new MappingGovernanceOptions();

        Assert.Equal("ModelRoleGovernance", MappingGovernanceOptions.SectionName);
        Assert.Equal(8, options.DecisionMaxAttempts);
        Assert.Equal(25, options.DecisionBaseDelayMs);
        Assert.Equal(1_000, options.DecisionMaxDelayMs);
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 50)]
    [InlineData(3, 100)]
    [InlineData(20, 1_000)]
    public void DelayFor_DoublesPerAttemptAndIsCapped(int attempt, double ceilingMs)
    {
        var options = new MappingGovernanceOptions();

        var shortest = MappingGovernanceBackoff.DelayFor(attempt, options, 0.0).TotalMilliseconds;
        var longest = MappingGovernanceBackoff.DelayFor(attempt, options, 0.999).TotalMilliseconds;

        // Jitter scales the delay into its upper half, so contending writers spread out.
        Assert.Equal(ceilingMs / 2, shortest, 3);
        Assert.InRange(longest, ceilingMs / 2, ceilingMs);
    }

    [Fact]
    public void NextJitter_IsInTheUnitInterval()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(MappingGovernanceBackoff.NextJitter(), 0.0, 0.999);
        }
    }
}
