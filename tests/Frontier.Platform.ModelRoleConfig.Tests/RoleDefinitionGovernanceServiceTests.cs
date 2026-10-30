using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 tests for <see cref="RoleDefinitionGovernanceService"/>: definitions governed by a
/// proposal a second principal decides, mirroring the mapping loop without rings. Every decision is
/// asserted to be recorded before it lands, and compensated when it does not.
/// </summary>
public sealed class RoleDefinitionGovernanceServiceTests
{
    [Fact]
    public async Task ProposeAsync_AgentBackedRoleWithBothRefs_RecordsAPendingUnstampedProposal()
    {
        var store = new FakeRoleDefinitionStore();
        var recorder = new FakeMappingDecisionRecorder();
        var stamped = RoleDefinitionSamples.AgentBacked(AgentRoleId);

        var proposal = await Service(store, recorder).ProposeAsync(stamped, Proposer, Reason, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Equal(FixedTimeProvider.Now, proposal.ProposedAtUtc);
        Assert.Null(proposal.PredecessorDefinitionVersion);
        // The caller's stamps are never trusted (ADR-PA32): hashes and approval come from the service.
        Assert.Equal(AgentDefinition(), proposal.ProposedDefinition);
        Assert.Equal(proposal, store.Proposal(proposal.ProposalId));
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal(RoleDefinitionGovernanceEventTypes.Proposed, decision.EventType);
        Assert.Equal(AgentRoleId, decision.RoleId);
        Assert.Equal(MappingProposalState.PendingApproval, decision.State);
    }

    [Fact]
    public async Task ProposeAsync_ModelBackedRoleWithNoRefs_IsAccepted_AndAsksTheCatalogueNothing()
    {
        var catalog = Catalog();
        var store = new FakeRoleDefinitionStore().WithDefinition(RoleDefinitionSamples.Migrated(ModelDefinition()));

        var proposal = await Service(store, new FakeMappingDecisionRecorder(), catalog)
            .ProposeAsync(ModelDefinition() with { Description = "Sharper wording" }, Proposer, Reason, CancellationToken.None);

        Assert.Equal(1, proposal.PredecessorDefinitionVersion);
        Assert.Empty(catalog.Lookups);
    }

    [Fact]
    public async Task ProposeAsync_ModelBackedRoleWithRefs_IsAccepted_AndApprovalStampsTheirHashes()
    {
        var store = new FakeRoleDefinitionStore();
        var withRefs = ModelDefinition() with { RoleId = "summarise", InputContractRef = InputRef, OutputContractRef = OutputRef };
        var service = Service(store, new FakeMappingDecisionRecorder());

        var proposal = await service.ProposeAsync(withRefs, Proposer, Reason, CancellationToken.None);
        var approved = await service.ApproveAsync("summarise", proposal.ProposalId, Approver, "reviewed", null, CancellationToken.None);

        Assert.Equal(FakeContractCatalog.HashOf(InputRef), approved.ProposedDefinition.InputContractHash);
        Assert.Equal(FakeContractCatalog.HashOf(OutputRef), store.Version(1)!.OutputContractHash);
    }

    [Theory]
    [InlineData(null, OutputRef)]
    [InlineData(InputRef, null)]
    [InlineData(null, null)]
    public async Task ProposeAsync_AgentBackedRoleMissingARef_IsRefused_AndNothingIsRecorded(string? input, string? output)
    {
        var recorder = new FakeMappingDecisionRecorder();

        var refusal = await Assert.ThrowsAsync<InvalidRoleDefinitionException>(() => Service(new FakeRoleDefinitionStore(), recorder)
            .ProposeAsync(AgentDefinition() with { InputContractRef = input, OutputContractRef = output }, Proposer, Reason, CancellationToken.None));

        Assert.Contains(refusal.Violations, violation => violation.Contains("both an input and an output contract ref", StringComparison.Ordinal));
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_MalformedRef_IsRefused() =>
        await Assert.ThrowsAsync<InvalidRoleDefinitionException>(() => Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder())
            .ProposeAsync(AgentDefinition() with { InputContractRef = "RecordPassInput" }, Proposer, Reason, CancellationToken.None));

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "genealogy/something-else/1.0")]
    public async Task ProposeAsync_RefThatDoesNotResolveOrIsRetired_IsRefused(bool retired, string? filedAs)
    {
        var catalog = new FakeContractCatalog().With(OutputRef);
        if (retired || filedAs is not null)
        {
            catalog.With(InputRef, retired, filedAs);
        }

        var recorder = new FakeMappingDecisionRecorder();
        var refusal = await Assert.ThrowsAsync<UnknownRoleContractException>(() =>
            Service(new FakeRoleDefinitionStore(), recorder, catalog).ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None));

        Assert.Contains(InputRef, Assert.Single(refusal.Violations), StringComparison.Ordinal);
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_ChangingAnExistingRolesBacking_IsRefused()
    {
        var store = new FakeRoleDefinitionStore().WithDefinition(RoleDefinitionSamples.Migrated(ModelDefinition()));
        var asAgent = AgentDefinition() with { RoleId = "deep-reasoning" };

        var refusal = await Assert.ThrowsAsync<RoleBackingChangeException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(asAgent, Proposer, Reason, CancellationToken.None));

        Assert.Contains("define a new role instead", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_SecondPendingProposalForTheRole_IsRefused_NamingTheOpenOneAndItsProposer()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());

        var refusal = await Assert.ThrowsAsync<MappingProposalAlreadyPendingException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(AgentDefinition(), "user:oid-other", Reason, CancellationToken.None));

        Assert.Contains("'d-1'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(Proposer, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProposeAsync_ADecidedProposalForTheRole_DoesNotHoldTheSlot()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending() with { State = MappingProposalState.Rejected });

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
    }

    [Fact]
    public async Task ProposeAsync_NullDefinition_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder()).ProposeAsync(null!, Proposer, Reason, CancellationToken.None));

    [Theory]
    [InlineData(" ", Reason)]
    [InlineData(Proposer, " ")]
    public async Task ProposeAsync_Unattributed_Throws(string proposer, string reason) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder()).ProposeAsync(AgentDefinition(), proposer, reason, CancellationToken.None));

    [Fact]
    public async Task ProposeAsync_AuditRecordCannotBeWritten_NothingIsStored()
    {
        var store = new FakeRoleDefinitionStore();
        var recorder = new FakeMappingDecisionRecorder { RecordThrows = new InvalidOperationException("audit down") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, recorder).ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None));

        Assert.Empty(store.Proposals);
    }

    [Fact]
    public async Task ProposeAsync_WriteFailsAfterTheRecord_RecordsACompensation()
    {
        var store = new FakeRoleDefinitionStore { CreateThrows = new InvalidOperationException("cosmos down") };
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store, recorder).ProposeAsync(AgentDefinition(), Proposer, Reason, CancellationToken.None));

        Assert.Single(recorder.Decisions);
        Assert.Equal(("record-1", "cosmos down"), Assert.Single(recorder.Compensations));
    }

    [Fact]
    public async Task ApproveAsync_NewRole_MintsV1_StampsHashesAndApproval_AndMovesThePointer()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var approved = await Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "contracts reviewed", store.Token("d-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Approved, approved.State);
        Assert.Equal(1, approved.DefinitionVersion);
        Assert.Equal(Approver, approved.ApprovedBy);
        Assert.Equal(store.Token("d-1"), approved.ConcurrencyToken);
        Assert.Equal(1, store.CurrentVersion);
        var minted = store.Version(1)!;
        Assert.Equal(FakeContractCatalog.HashOf(InputRef), minted.InputContractHash);
        Assert.Equal(FakeContractCatalog.HashOf(OutputRef), minted.OutputContractHash);
        Assert.Equal((1, Approver, FixedTimeProvider.Now), (minted.DefinitionVersion, minted.ApprovedBy, minted.EffectiveFromUtc));
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal((RoleDefinitionGovernanceEventTypes.Approved, (int?)null, (int?)1), (decision.EventType, decision.FromVersion, decision.ToVersion));
    }

    [Fact]
    public async Task ApproveAsync_ExistingRole_AllocatesTheNextVersion_FromTheCurrentOne()
    {
        var current = RoleDefinitionSamples.AgentBacked(AgentRoleId);
        var store = new FakeRoleDefinitionStore().WithDefinition(current).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var approved = await Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None);

        Assert.Equal(3, approved.DefinitionVersion);
        Assert.Equal((2, 3), (Assert.Single(recorder.Decisions).FromVersion, Assert.Single(recorder.Decisions).ToVersion));
    }

    [Fact]
    public async Task ApproveAsync_ContractRetiredSincePropose_IsRefused_AndNothingIsWritten()
    {
        var catalog = Catalog();
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        catalog.Retire(OutputRef);
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<UnknownRoleContractException>(() =>
            Service(store, recorder, catalog).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));

        Assert.Null(store.Version(1));
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ApproveAsync_BackingChangedUnderneathTheProposal_IsRefused()
    {
        var store = new FakeRoleDefinitionStore()
            .WithDefinition(RoleDefinitionSamples.Migrated(ModelDefinition()) with { RoleId = AgentRoleId })
            .WithProposal(Pending());

        await Assert.ThrowsAsync<RoleBackingChangeException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_ByTheProposer_IsRefused() =>
        await Assert.ThrowsAsync<DistinctApproverRequiredException>(() =>
            Service(new FakeRoleDefinitionStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .ApproveAsync(AgentRoleId, "d-1", Proposer.ToUpperInvariant(), "self", null, CancellationToken.None));

    [Fact]
    public async Task ApproveAsync_UnknownProposal_IsRefused() =>
        await Assert.ThrowsAsync<UnknownMappingProposalException>(() =>
            Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder())
                .ApproveAsync(AgentRoleId, "missing", Approver, "ok", null, CancellationToken.None));

    [Fact]
    public async Task ApproveAsync_AlreadyDecided_IsRefused() =>
        await Assert.ThrowsAsync<IllegalMappingTransitionException>(() =>
            Service(new FakeRoleDefinitionStore().WithProposal(Pending() with { State = MappingProposalState.Withdrawn }), new FakeMappingDecisionRecorder())
                .ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));

    [Fact]
    public async Task ApproveAsync_StaleToken_IsRefused_AndNeverRetried()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending() with { DecidedBy = "user:oid-someone" });
        var recorder = new FakeMappingDecisionRecorder();

        var refusal = await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", "etag-stale", CancellationToken.None));

        Assert.Contains("decided by 'user:oid-someone'", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ApproveAsync_ConcurrentWriterTookTheVersion_ReallocatesAndCompensates()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();
        var raced = false;
        store.BeforeAllocate = () =>
        {
            if (!raced)
            {
                raced = true;
                store.WithDefinition(RoleDefinitionSamples.AgentBacked(AgentRoleId) with { DefinitionVersion = 1 }, current: false);
            }

            return Task.CompletedTask;
        };

        var approved = await Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None);

        Assert.Equal(2, approved.DefinitionVersion);
        Assert.Equal(2, recorder.Decisions.Count);
        Assert.Contains(recorder.Compensations, entry => entry.Reason.Contains("another writer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_AlwaysLosesTheRace_ThrowsAfterTheAttemptsAreExhausted()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();
        store.BeforeAllocate = () => store.CreateProposalAsync(Pending(), CancellationToken.None);

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));

        Assert.Equal(FastRetry.DecisionMaxAttempts, recorder.Compensations.Count);
    }

    [Fact]
    public async Task ApproveAsync_BatchThrows_CompensatesAndRethrows()
    {
        var store = new FakeRoleDefinitionStore { AllocateThrows = new InvalidOperationException("batch failed") }.WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));

        Assert.Equal(("record-1", "batch failed"), Assert.Single(recorder.Compensations));
        Assert.Equal(MappingProposalState.PendingApproval, store.Proposal("d-1")!.State);
    }

    [Fact]
    public async Task ApproveAsync_AuditRecordCannotBeWritten_NothingIsStored()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder { RecordThrows = new InvalidOperationException("audit down") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(store, recorder).ApproveAsync(AgentRoleId, "d-1", Approver, "ok", null, CancellationToken.None));

        Assert.Null(store.Version(1));
    }

    [Theory]
    [InlineData(" ", "d-1", Approver, "ok")]
    [InlineData(AgentRoleId, " ", Approver, "ok")]
    [InlineData(AgentRoleId, "d-1", " ", "ok")]
    [InlineData(AgentRoleId, "d-1", Approver, " ")]
    public async Task Decisions_WithoutRoleProposalActorOrReason_Throw(string roleId, string proposalId, string actor, string reason)
    {
        var service = Service(new FakeRoleDefinitionStore().WithProposal(Pending()), new FakeMappingDecisionRecorder());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ApproveAsync(roleId, proposalId, actor, reason, null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RejectAsync(roleId, proposalId, actor, reason, null, CancellationToken.None));
    }

    [Fact]
    public async Task RejectAsync_ByAnotherPrincipal_IsTerminal_AndRecordsTheRejection()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var rejected = await Service(store, recorder).RejectAsync(AgentRoleId, "d-1", Approver, "contract too loose", store.Token("d-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
        Assert.Equal((Approver, "contract too loose"), (rejected.DecidedBy, rejected.DecisionReason));
        Assert.Equal(store.Token("d-1"), rejected.ConcurrencyToken);
        Assert.Equal(RoleDefinitionGovernanceEventTypes.Rejected, Assert.Single(recorder.Decisions).EventType);
        Assert.Null(store.CurrentVersion);
    }

    [Fact]
    public async Task RejectAsync_ByTheProposer_IsRefused() =>
        await Assert.ThrowsAsync<DistinctApproverRequiredException>(() =>
            Service(new FakeRoleDefinitionStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .RejectAsync(AgentRoleId, "d-1", Proposer, "self", null, CancellationToken.None));

    [Fact]
    public async Task WithdrawAsync_ByTheProposer_IsTerminal_AndRecordsTheWithdrawal()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var withdrawn = await Service(store, recorder).WithdrawAsync(AgentRoleId, "d-1", Proposer, "wrong contract", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Withdrawn, withdrawn.State);
        Assert.Equal(RoleDefinitionGovernanceEventTypes.Withdrawn, Assert.Single(recorder.Decisions).EventType);
    }

    [Fact]
    public async Task WithdrawAsync_ByAnyoneButTheProposer_IsRefused() =>
        await Assert.ThrowsAsync<ProposerOnlyWithdrawalException>(() =>
            Service(new FakeRoleDefinitionStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .WithdrawAsync(AgentRoleId, "d-1", Approver, "not mine", null, CancellationToken.None));

    [Fact]
    public async Task WithdrawAsync_StaleToken_IsRefused() =>
        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(new FakeRoleDefinitionStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .WithdrawAsync(AgentRoleId, "d-1", Proposer, "no", "etag-stale", CancellationToken.None));

    [Fact]
    public async Task RejectAsync_LosesARaceThenWins_CompensatesTheLostAttempt()
    {
        var store = new RacingReplaceStore(losses: 1);
        store.WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var rejected = await Service(store, recorder).RejectAsync(AgentRoleId, "d-1", Approver, "no", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
        Assert.Equal(2, recorder.Decisions.Count);
        Assert.Contains("another decision", Assert.Single(recorder.Compensations).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectAsync_AlwaysLosesTheRace_ThrowsAfterTheAttemptsAreExhausted()
    {
        var store = new RacingReplaceStore(losses: int.MaxValue);
        store.WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<MappingGovernanceException>(() =>
            Service(store, recorder).RejectAsync(AgentRoleId, "d-1", Approver, "no", null, CancellationToken.None));

        Assert.Equal(FastRetry.DecisionMaxAttempts, recorder.Compensations.Count);
    }

    [Fact]
    public async Task GetProposalAsync_CarriesTheToken_AndIsNullWhenAbsent()
    {
        var store = new FakeRoleDefinitionStore().WithProposal(Pending());
        var service = Service(store, new FakeMappingDecisionRecorder());

        Assert.Equal(store.Token("d-1"), (await service.GetProposalAsync(AgentRoleId, "d-1", CancellationToken.None))!.ConcurrencyToken);
        Assert.Null(await service.GetProposalAsync(AgentRoleId, "missing", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetProposalAsync(" ", "d-1", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetProposalAsync(AgentRoleId, " ", CancellationToken.None));
    }

    [Fact]
    public async Task ListProposalsAsync_FiltersByRoleAndState_AsTheMappingListDoes()
    {
        var store = new FakeRoleDefinitionStore()
            .WithProposal(Pending())
            .WithProposal(Pending(ModelDefinition(), "d-2") with { State = MappingProposalState.Rejected });
        var service = Service(store, new FakeMappingDecisionRecorder());

        var page = await service.ListProposalsAsync(new MappingProposalQuery { States = [MappingProposalState.PendingApproval] }, CancellationToken.None);

        Assert.Equal("d-1", Assert.Single(page.Proposals).ProposalId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MappingProposalQuery.MaxPageSize + 1)]
    public async Task ListProposalsAsync_PageSizeOutOfRange_Throws(int pageSize) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder())
                .ListProposalsAsync(new MappingProposalQuery { PageSize = pageSize }, CancellationToken.None));

    [Fact]
    public async Task ListProposalsAsync_NullQuery_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Service(new FakeRoleDefinitionStore(), new FakeMappingDecisionRecorder()).ListProposalsAsync(null!, CancellationToken.None));

    [Fact]
    public void ARefusal_IsPermanent()
    {
        // Invariant 7: every governance refusal is a contract violation, never retried.
        Assert.IsAssignableFrom<ContractViolationException>(new InvalidRoleDefinitionException("t", ["v"]));
        Assert.IsAssignableFrom<ContractViolationException>(new UnknownRoleContractException("t", ["v"]));
        Assert.IsAssignableFrom<MappingGovernanceRefusalException>(new RoleBackingChangeException("t", ["v"]));
    }

    /// <summary>A store whose conditional proposal replace loses the first <c>losses</c> races.</summary>
    private sealed class RacingReplaceStore(int losses) : IRoleDefinitionStore
    {
        private readonly FakeRoleDefinitionStore inner = new();
        private int lost;

        internal void WithProposal(RoleDefinitionProposal proposal) => inner.WithProposal(proposal);

        public Task<RoleDefinition?> FindCurrentDefinitionAsync(string roleId, CancellationToken cancellationToken) => inner.FindCurrentDefinitionAsync(roleId, cancellationToken);

        public Task<IReadOnlyList<int>> ListDefinitionVersionsAsync(string roleId, CancellationToken cancellationToken) => inner.ListDefinitionVersionsAsync(roleId, cancellationToken);

        public Task<StoredRoleDefinitionProposal?> FindUndecidedProposalAsync(string roleId, CancellationToken cancellationToken) => inner.FindUndecidedProposalAsync(roleId, cancellationToken);

        public Task<StoredRoleDefinitionProposal?> FindProposalAsync(string roleId, string proposalId, CancellationToken cancellationToken) => inner.FindProposalAsync(roleId, proposalId, cancellationToken);

        public Task CreateProposalAsync(RoleDefinitionProposal proposal, CancellationToken cancellationToken) => inner.CreateProposalAsync(proposal, cancellationToken);

        public Task<string?> TryReplaceProposalAsync(RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken) =>
            lost++ < losses ? Task.FromResult<string?>(null) : inner.TryReplaceProposalAsync(proposal, expectedETag, cancellationToken);

        public Task<string?> TryAllocateDefinitionAsync(RoleDefinition definition, RoleDefinitionProposal proposal, string expectedETag, CancellationToken cancellationToken) =>
            inner.TryAllocateDefinitionAsync(definition, proposal, expectedETag, cancellationToken);

        public Task<RoleDefinitionProposalPage> QueryProposalsAsync(MappingProposalQuery query, CancellationToken cancellationToken) => inner.QueryProposalsAsync(query, cancellationToken);
    }
}
