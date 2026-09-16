using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 tests for <see cref="MappingGovernanceService"/> (doc 08 §7-8, ADR-M3, ADR-PA32): the
/// propose → approve → promote loop, its refusals, the concurrency-safe version allocation, and the
/// hardened rollback. Every decision is asserted to be audited <b>before</b> it is made.
/// </summary>
public sealed class MappingGovernanceServiceTests
{
    [Fact]
    public async Task ProposeAsync_ValidChange_RecordsAPendingProposalStampedByTheService()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true);
        var recorder = new FakeMappingDecisionRecorder();

        var proposal = await Service(store, recorder).ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Equal(Proposer, proposal.ProposedBy);
        Assert.Equal(FixedTimeProvider.Now, proposal.ProposedAtUtc);
        // The caller's version/approver/effective-at are never trusted (Mark's call, 2026-09-15).
        Assert.Equal(0, proposal.Change.ProposedMapping.MappingVersion);
        Assert.Equal(string.Empty, proposal.Change.ProposedMapping.ApprovedBy);
        Assert.Null(proposal.MappingVersion);
        Assert.Equal(MappingGovernanceEventTypes.Proposed, Assert.Single(recorder.Decisions).EventType);
    }

    [Fact]
    public async Task ProposeAsync_SetsPredecessorFleetVersionAtProposeTime()
    {
        // The S6.6 verdict: what an engagement outside the canary keeps being served.
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true);

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Equal(1, proposal.PredecessorFleetVersion);
        Assert.Equal(1, proposal.Change.ProposedMapping.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_CurrentIsCanary_TakesItsFleetPredecessor()
    {
        var canary = FleetV1 with { MappingVersion = 5, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 };
        var store = new FakeMappingProposalStore().WithVersion(FleetV1).WithVersion(canary, current: true);

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Equal(1, proposal.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_RoleHasNoCurrentPointer_HasNoPredecessor()
    {
        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
            .ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Null(proposal.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_CurrentPointsAtAMissingVersion_HasNoPredecessor()
    {
        // A pointer whose version document is gone must not fabricate a predecessor.
        var dangling = new DanglingPointerStore();

        var proposal = await new MappingGovernanceService(
                dangling, new FakeRoleMappingWriter(), new FakeMappingDecisionRecorder(), new FixedTimeProvider(FixedTimeProvider.Now),
                Microsoft.Extensions.Options.Options.Create(FastRetry))
            .ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Null(proposal.PredecessorFleetVersion);
    }

    [Fact]
    public async Task ProposeAsync_NullChange_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).ProposeAsync(null!, Proposer, CancellationToken.None));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProposeAsync_UnattributedProposer_Throws(string proposer) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).ProposeAsync(Change(), proposer, CancellationToken.None));

    [Fact]
    public async Task ProposeAsync_NoReason_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).ProposeAsync(Change(reason: " "), Proposer, CancellationToken.None));

    [Fact]
    public async Task ProposeAsync_AuditRecordCannotBeWritten_NothingIsStored()
    {
        // Fail closed (K6): the record is appended before the change, so an unwritable record stops it.
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true);
        var recorder = new FakeMappingDecisionRecorder { RecordThrows = new InvalidOperationException("audit down") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, recorder).ProposeAsync(Change(), Proposer, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_AllocatesTheNextVersionAndMakesItCurrent()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var approved = await Service(store, recorder).ApproveAsync(RoleId, "p-1", Approver, "eval evidence accepted", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Approved, approved.State);
        Assert.Equal(2, approved.MappingVersion);
        Assert.Equal(Approver, approved.ApprovedBy);
        Assert.Equal(FixedTimeProvider.Now, approved.EffectiveFromUtc);
        Assert.Equal(RolloutRing.Canary, store.Version(2)!.Ring);
        Assert.Equal(2, store.CurrentVersion);
        Assert.Equal(MappingGovernanceEventTypes.Approved, Assert.Single(recorder.Decisions).EventType);
    }

    [Fact]
    public async Task ApproveAsync_FirstEverVersion_AllocatesV1()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var approved = await Service(store, new FakeMappingDecisionRecorder())
            .ApproveAsync(RoleId, "p-1", Approver, "first mapping", null, CancellationToken.None);

        Assert.Equal(1, approved.MappingVersion);
    }

    [Fact]
    public async Task ApproveAsync_ProposerApprovingTheirOwnChange_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ApproveAsync(RoleId, "p-1", Proposer, "self approval", null, CancellationToken.None));

        Assert.Contains("cannot also decide it", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Version(2));
    }

    [Fact]
    public async Task ApproveAsync_UnknownProposal_IsRefused() =>
        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .ApproveAsync(RoleId, "missing", Approver, "reason", null, CancellationToken.None));

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_IsRefused()
    {
        var approved = Pending() with { State = MappingProposalState.Approved, MappingVersion = 2, ApprovedBy = Approver };
        var store = new FakeMappingProposalStore().WithProposal(approved);

        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ApproveAsync(RoleId, "p-1", Approver, "again", null, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_ConcurrentApprovalTookTheVersion_ReallocatesAndCompensates()
    {
        // Two approvals cannot both write {role}:v{n}: the create-only id is the allocation.
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();
        var raced = false;
        store.BeforeAllocate = () =>
        {
            if (!raced)
            {
                raced = true;
                store.WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary });
            }

            return Task.CompletedTask;
        };

        var approved = await Service(store, recorder).ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None);

        Assert.Equal(3, approved.MappingVersion);
        Assert.Equal(2, recorder.Decisions.Count);
        Assert.Contains(recorder.Compensations, entry => entry.Reason.Contains("another writer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_AlwaysLosesTheRace_ThrowsAfterTheAttemptsAreExhausted()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();
        // Re-storing the proposal bumps its ETag, so the conditional replace always loses.
        store.BeforeAllocate = () => store.CreateProposalAsync(Pending(), CancellationToken.None);

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            Service(store, recorder).ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None));

        Assert.Equal(FastRetry.DecisionMaxAttempts, recorder.Compensations.Count);
    }

    [Fact]
    public async Task ApproveAsync_StoreThrows_CompensatesTheRecordAndRethrows()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending()) ;
        store.AllocateThrows = new InvalidOperationException("cosmos down");
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, recorder).ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None));

        Assert.Contains(recorder.Compensations, entry => entry.Reason.Contains("cosmos down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_NeverProducesAShadowVersionAndNeverPointsCurrentAtOne()
    {
        // S13.101: a shadow version must never become current — it would serve an unevaluated chain
        // and trip RoleCatalogueCheck at the next boot.
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());

        var approved = await Service(store, new FakeMappingDecisionRecorder())
            .ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None);

        Assert.NotEqual(RolloutRing.Shadow, store.Version(approved.MappingVersion!.Value)!.Ring);
        Assert.All(store.MadeCurrent, version => Assert.NotEqual(RolloutRing.Shadow, store.Version(version)!.Ring));
    }

    [Fact]
    public async Task PromoteAsync_AppendsAFleetVersionCarryingTheSameChain()
    {
        var approved = Pending() with { State = MappingProposalState.Approved, MappingVersion = 2, ApprovedBy = Approver };
        var store = new FakeMappingProposalStore().WithVersion(FleetV1).WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true).WithProposal(approved);
        var recorder = new FakeMappingDecisionRecorder();

        var promoted = await Service(store, recorder).PromoteAsync(RoleId, "p-1", Approver, "7 clean days", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Promoted, promoted.State);
        Assert.Equal(3, promoted.PromotedVersion);
        var fleet = store.Version(3)!;
        Assert.Equal(RolloutRing.Fleet, fleet.Ring);
        Assert.Equal(0, fleet.CanaryPercent);
        Assert.Null(fleet.PredecessorFleetVersion);
        Assert.Equal(3, store.CurrentVersion);
        Assert.Equal(MappingGovernanceEventTypes.Promoted, Assert.Single(recorder.Decisions).EventType);
    }

    [Fact]
    public async Task PromoteAsync_PendingProposal_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).PromoteAsync(RoleId, "p-1", Approver, "too early", null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_MovesToTerminalAndAudits()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var rejected = await Service(store, recorder).RejectAsync(RoleId, "p-1", Approver, "no evidence", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
        Assert.True(rejected.State.IsTerminal);
        Assert.Equal(Approver, rejected.DecidedBy);
        Assert.Equal("no evidence", rejected.DecisionReason);
        Assert.Equal(MappingGovernanceEventTypes.Rejected, Assert.Single(recorder.Decisions).EventType);
        Assert.Null(store.Version(2));
    }

    [Fact]
    public async Task WithdrawAsync_MovesToTerminalAndAudits()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var withdrawn = await Service(store, recorder).WithdrawAsync(RoleId, "p-1", Proposer, "superseded", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Withdrawn, withdrawn.State);
        Assert.Equal(MappingGovernanceEventTypes.Withdrawn, Assert.Single(recorder.Decisions).EventType);
    }

    [Fact]
    public async Task RejectAsync_AlreadyRejected_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending() with { State = MappingProposalState.Rejected });

        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).RejectAsync(RoleId, "p-1", Approver, "again", null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_AlwaysLosesTheRace_ThrowsAfterTheAttemptsAreExhausted()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        // Every read hands back a token the write will reject, so the decision can never land.
        var service = new MappingGovernanceService(
            new EtagChurningStore(store), new FakeRoleMappingWriter(), recorder, new FixedTimeProvider(FixedTimeProvider.Now),
            Microsoft.Extensions.Options.Options.Create(FastRetry));

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            service.RejectAsync(RoleId, "p-1", Approver, "no", null, CancellationToken.None));

        Assert.Equal(FastRetry.DecisionMaxAttempts, recorder.Compensations.Count);
    }

    [Fact]
    public async Task RollbackToVersionAsync_ValidTarget_RepointsCurrentAndReportsWhatChanged()
    {
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1)
            .WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true);
        var writer = new FakeRoleMappingWriter();
        var recorder = new FakeMappingDecisionRecorder();

        var result = await Service(store, recorder, writer)
            .RollbackToVersionAsync(RoleId, 1, Approver, "validator pass rate 0.75 to 0.62", CancellationToken.None);

        Assert.Equal(2, result.PreviousVersion);
        Assert.Equal(1, result.CurrentVersion);
        Assert.Equal(FixedTimeProvider.Now, result.EffectiveAtUtc);
        Assert.Equal(RolloutRing.Fleet, result.Ring);
        Assert.Equal((RoleId, 1), Assert.Single(writer.Writes));
        Assert.Equal(MappingGovernanceEventTypes.RolledBack, recorder.Decisions[0].EventType);
    }

    [Fact]
    public async Task RollbackToVersionAsync_MissingTarget_IsRefusedAndWritesNothing()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true);
        var writer = new FakeRoleMappingWriter();

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder(), writer).RollbackToVersionAsync(RoleId, 99, Approver, "revert", CancellationToken.None));

        Assert.Contains("no mapping version 99", exception.Message, StringComparison.Ordinal);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShadowTarget_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithVersion(ShadowV2);
        var writer = new FakeRoleMappingWriter();

        var exception = await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder(), writer).RollbackToVersionAsync(RoleId, 2, Approver, "revert", CancellationToken.None));

        Assert.Contains("shadow version", exception.Message, StringComparison.Ordinal);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task RollbackToVersionAsync_NoCurrentPointer_IsRefused()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1);

        await Assert.ThrowsAnyAsync<ContractViolationException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).RollbackToVersionAsync(RoleId, 1, Approver, "revert", CancellationToken.None));
    }

    [Theory]
    [InlineData("", "reason")]
    [InlineData("actor", "")]
    public async Task RollbackToVersionAsync_Unattributed_Throws(string actor, string reason) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .RollbackToVersionAsync(RoleId, 1, actor, reason, CancellationToken.None));

    [Fact]
    public async Task RollbackToVersionAsync_BlankRole_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .RollbackToVersionAsync(" ", 1, Approver, "revert", CancellationToken.None));

    [Fact]
    public async Task RollbackToVersionAsync_PointerWriteFails_CompensatesTheRecord()
    {
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1)
            .WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true);
        var writer = new FakeRoleMappingWriter { Throws = new InvalidOperationException("pointer write failed") };
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, recorder, writer).RollbackToVersionAsync(RoleId, 1, Approver, "revert", CancellationToken.None));

        Assert.Contains(recorder.Compensations, entry => entry.Reason.Contains("pointer write failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RollbackToVersionAsync_MovesTheOwningProposalToRolledBack()
    {
        var approved = Pending() with { State = MappingProposalState.Approved, MappingVersion = 2, ApprovedBy = Approver };
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1)
            .WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true)
            .WithProposal(approved);

        await Service(store, new FakeMappingDecisionRecorder(), new FakeRoleMappingWriter())
            .RollbackToVersionAsync(RoleId, 1, Approver, "regression", CancellationToken.None);

        var stored = store.Proposal("p-1")!;
        Assert.Equal(MappingProposalState.RolledBack, stored.State);
        Assert.Equal(2, stored.RolledBackToVersion);
    }

    [Fact]
    public async Task RollbackToVersionAsync_NoOwningProposal_StillRollsBack()
    {
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1)
            .WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true);
        var writer = new FakeRoleMappingWriter();

        var result = await Service(store, new FakeMappingDecisionRecorder(), writer)
            .RollbackToVersionAsync(RoleId, 1, Approver, "regression", CancellationToken.None);

        Assert.Equal(1, result.CurrentVersion);
        Assert.Single(writer.Writes);
    }

    [Fact]
    public async Task GetProposalAsync_KnownAndUnknown()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());
        var service = Service(store, new FakeMappingDecisionRecorder());

        Assert.NotNull(await service.GetProposalAsync(RoleId, "p-1", CancellationToken.None));
        Assert.Null(await service.GetProposalAsync(RoleId, "nope", CancellationToken.None));
    }

    [Theory]
    [InlineData("", "p-1")]
    [InlineData("role", "")]
    public async Task GetProposalAsync_BlankArguments_Throw(string roleId, string proposalId) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).GetProposalAsync(roleId, proposalId, CancellationToken.None));

    [Fact]
    public async Task ListProposalsAsync_FiltersByState()
    {
        var store = new FakeMappingProposalStore()
            .WithProposal(Pending())
            .WithProposal(Pending("p-2") with { State = MappingProposalState.Rejected });
        var service = Service(store, new FakeMappingDecisionRecorder());

        var pending = await service.ListProposalsAsync(
            new MappingProposalQuery { RoleId = RoleId, States = [MappingProposalState.PendingApproval] }, CancellationToken.None);

        Assert.Equal("p-1", Assert.Single(pending.Proposals).ProposalId);
    }

    [Fact]
    public async Task ListProposalsAsync_NullQuery_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder()).ListProposalsAsync(null!, CancellationToken.None));

    [Theory]
    [InlineData(0)]
    [InlineData(MappingProposalQuery.MaxPageSize + 1)]
    public async Task ListProposalsAsync_InvalidPageSize_Throws(int pageSize) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .ListProposalsAsync(new MappingProposalQuery { RoleId = "role", PageSize = pageSize }, CancellationToken.None));

    [Fact]
    public async Task ObsoleteMembers_DirectToTheirReplacements()
    {
        var service = Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder());

#pragma warning disable CS0618 // deliberately exercising the obsolete surface
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ProposeChangeAsync(Change(), CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.ApproveAsync("p-1", Approver, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.RollbackAsync(RoleId, 1, "reason", CancellationToken.None));
#pragma warning restore CS0618
    }

    /// <summary>A store whose <c>current</c> pointer names a version document that does not exist.</summary>
    private sealed class DanglingPointerStore : IMappingProposalStore
    {
        private readonly FakeMappingProposalStore inner = new();

        public Task<IReadOnlyList<int>> ListMappingVersionsAsync(string roleId, CancellationToken ct) => inner.ListMappingVersionsAsync(roleId, ct);

        public Task<RoleMapping?> FindMappingVersionAsync(string roleId, int version, CancellationToken ct) => Task.FromResult<RoleMapping?>(null);

        public Task<int?> FindCurrentVersionAsync(string roleId, CancellationToken ct) => Task.FromResult<int?>(7);

        public Task<StoredMappingProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken ct) => inner.FindProposalAsync(roleId, proposalId, ct);

        public Task<StoredMappingProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken ct) => inner.FindUndecidedProposalAsync(roleId, ct);

        public Task<StoredMappingProposal?> FindProposalByLiveVersionAsync(string roleId, int version, CancellationToken ct) => inner.FindProposalByLiveVersionAsync(roleId, version, ct);

        public Task CreateProposalAsync(MappingChangeProposal proposal, CancellationToken ct) => inner.CreateProposalAsync(proposal, ct);

        public Task<string?> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken ct) =>
            inner.TryReplaceProposalAsync(proposal, expectedETag, ct);

        public Task<IReadOnlyList<RoleMapping>> ListMappingsAsync(string roleId, CancellationToken ct) => inner.ListMappingsAsync(roleId, ct);

        public Task<string?> TryAllocateVersionAsync(RoleMapping mapping, MappingChangeProposal proposal, string expectedETag, bool makeCurrent, CancellationToken ct) =>
            inner.TryAllocateVersionAsync(mapping, proposal, expectedETag, makeCurrent, ct);

        public Task<MappingProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken ct) => inner.QueryProposalsAsync(query, ct);
    }

    /// <summary>Wraps a store so every proposal read hands back a token the next write will reject.</summary>
    private sealed class EtagChurningStore(FakeMappingProposalStore inner) : IMappingProposalStore
    {
        public Task<IReadOnlyList<int>> ListMappingVersionsAsync(string roleId, CancellationToken ct) => inner.ListMappingVersionsAsync(roleId, ct);

        public Task<RoleMapping?> FindMappingVersionAsync(string roleId, int version, CancellationToken ct) => inner.FindMappingVersionAsync(roleId, version, ct);

        public Task<int?> FindCurrentVersionAsync(string roleId, CancellationToken ct) => inner.FindCurrentVersionAsync(roleId, ct);

        public async Task<StoredMappingProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken ct)
        {
            var stored = await inner.FindProposalAsync(roleId, proposalId, ct);
            return stored is null ? null : stored with { ETag = "stale" };
        }

        public Task<StoredMappingProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken ct) => inner.FindUndecidedProposalAsync(roleId, ct);

        public Task<StoredMappingProposal?> FindProposalByLiveVersionAsync(string roleId, int version, CancellationToken ct) => inner.FindProposalByLiveVersionAsync(roleId, version, ct);

        public Task CreateProposalAsync(MappingChangeProposal proposal, CancellationToken ct) => inner.CreateProposalAsync(proposal, ct);

        public Task<string?> TryReplaceProposalAsync(MappingChangeProposal proposal, string expectedETag, CancellationToken ct) =>
            inner.TryReplaceProposalAsync(proposal, expectedETag, ct);

        public Task<IReadOnlyList<RoleMapping>> ListMappingsAsync(string roleId, CancellationToken ct) => inner.ListMappingsAsync(roleId, ct);

        public Task<string?> TryAllocateVersionAsync(RoleMapping mapping, MappingChangeProposal proposal, string expectedETag, bool makeCurrent, CancellationToken ct) =>
            inner.TryAllocateVersionAsync(mapping, proposal, expectedETag, makeCurrent, ct);

        public Task<MappingProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken ct) => inner.QueryProposalsAsync(query, ct);
    }
}
