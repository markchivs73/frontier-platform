using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// S13.101 tests for the ADR-PA34 surface: the proposal's concurrency token and the decisions that
/// accept it, the relaxed proposals query, public version listing, and the typed refusals.
/// </summary>
public sealed class MappingConcurrencyTokenTests
{
    private const string StaleToken = "etag-somebody-else-decided";

    [Fact]
    public async Task ReadingAProposal_CarriesTheTokenTheNextDecisionMustMatch()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var proposal = await Service(store, new FakeMappingDecisionRecorder()).GetProposalAsync(RoleId, "p-1", CancellationToken.None);

        Assert.Equal(store.Token("p-1"), proposal!.ConcurrencyToken);
    }

    [Fact]
    public async Task ApproveAsync_WithTheFreshToken_Succeeds()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());

        var approved = await Service(store, new FakeMappingDecisionRecorder())
            .ApproveAsync(RoleId, "p-1", Approver, "eval accepted", store.Token("p-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Approved, approved.State);
        // The decision hands back the token it minted, so the next decision needs no re-read.
        Assert.Equal(store.Token("p-1"), approved.ConcurrencyToken);
    }

    [Fact]
    public async Task ApproveAsync_WithAStaleToken_IsRefusedAndChangesNothing()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending());
        var recorder = new FakeMappingDecisionRecorder();

        var exception = await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, recorder).ApproveAsync(RoleId, "p-1", Approver, "eval accepted", StaleToken, CancellationToken.None));

        Assert.Contains("changed while it was being decided", exception.Message, StringComparison.Ordinal);
        Assert.Equal(MappingProposalState.PendingApproval, store.Proposal("p-1")!.State);
        Assert.Null(store.Version(2));
        // A refusal is not a decision: nothing is audited and nothing is compensated.
        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task RejectAsync_WithTheFreshTokenAndWithAStaleOne()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).RejectAsync(RoleId, "p-1", Approver, "no", StaleToken, CancellationToken.None));

        var rejected = await Service(store, new FakeMappingDecisionRecorder())
            .RejectAsync(RoleId, "p-1", Approver, "no evidence", store.Token("p-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
        Assert.Equal(store.Token("p-1"), rejected.ConcurrencyToken);
    }

    [Fact]
    public async Task WithdrawAsync_WithTheFreshTokenAndWithAStaleOne()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).WithdrawAsync(RoleId, "p-1", Proposer, "mine", StaleToken, CancellationToken.None));

        var withdrawn = await Service(store, new FakeMappingDecisionRecorder())
            .WithdrawAsync(RoleId, "p-1", Proposer, "superseded", store.Token("p-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Withdrawn, withdrawn.State);
    }

    [Fact]
    public async Task PromoteAsync_WithTheFreshTokenAndWithAStaleOne()
    {
        var approved = Pending() with { State = MappingProposalState.Approved, MappingVersion = 2, ApprovedBy = Approver };
        var store = new FakeMappingProposalStore()
            .WithVersion(FleetV1)
            .WithVersion(FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, PredecessorFleetVersion = 1 }, current: true)
            .WithProposal(approved);

        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).PromoteAsync(RoleId, "p-1", Approver, "clean", StaleToken, CancellationToken.None));

        var promoted = await Service(store, new FakeMappingDecisionRecorder())
            .PromoteAsync(RoleId, "p-1", Approver, "7 clean days", store.Token("p-1"), CancellationToken.None);

        Assert.Equal(MappingProposalState.Promoted, promoted.State);
    }

    [Fact]
    public async Task AStaleTokenIsReportedBeforeTheTransitionRule()
    {
        // A caller reasoning about a state that no longer exists has had every later judgement
        // invalidated too, so staleness is what it must be told about first (ADR-PA34).
        var store = new FakeMappingProposalStore().WithProposal(Pending() with { State = MappingProposalState.Rejected });

        await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ApproveAsync(RoleId, "p-1", Approver, "too late", StaleToken, CancellationToken.None));
    }

    [Fact]
    public async Task TheStaleTokenMessageNamesWhoDecidedIt()
    {
        var decided = Pending() with { State = MappingProposalState.Rejected, DecidedBy = Approver };
        var store = new FakeMappingProposalStore().WithProposal(decided);

        var exception = await Assert.ThrowsAsync<MappingConcurrencyConflictException>(() =>
            Service(store, new FakeMappingDecisionRecorder()).ApproveAsync(RoleId, "p-1", Proposer, "too late", StaleToken, CancellationToken.None));

        Assert.Contains("rejected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Approver, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVersionAllocationRace_StillRetries_EvenWithAnExpectedTokenSupplied()
    {
        // The distinction ADR-PA34 rests on. Losing the version race does not move the proposal, so
        // the caller's token stays valid and the decision it asked for is still delivered - at v3.
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

        var approved = await Service(store, recorder)
            .ApproveAsync(RoleId, "p-1", Approver, "eval accepted", store.Token("p-1"), CancellationToken.None);

        Assert.Equal(3, approved.MappingVersion);
        Assert.Equal(2, recorder.Decisions.Count);
    }

    [Fact]
    public async Task ADecisionWithNoExpectedToken_IsUnguardedByDesign()
    {
        // A caller with no prior view has nothing to be stale about (ADR-PA34).
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        var rejected = await Service(store, new FakeMappingDecisionRecorder())
            .RejectAsync(RoleId, "p-1", Approver, "no evidence", null, CancellationToken.None);

        Assert.Equal(MappingProposalState.Rejected, rejected.State);
    }
}

/// <summary>S13.101 tests for the live-version rule a decision records as its <c>from</c> version.</summary>
public sealed class MappingLiveVersionTests
{
    [Fact]
    public void APromotedProposalsLiveVersion_IsItsFleetVersion()
    {
        // Promotion mints a new fleet version, so that - not the canary it supersedes - is what the
        // proposal now has in front of new executions.
        var promoted = Pending() with { State = MappingProposalState.Promoted, MappingVersion = 2, PromotedVersion = 3 };

        Assert.Equal(3, MappingGovernanceService.LiveVersionOf(promoted));
    }

    [Fact]
    public void AnApprovedProposalsLiveVersion_IsItsCanaryVersion() =>
        Assert.Equal(2, MappingGovernanceService.LiveVersionOf(Pending() with { State = MappingProposalState.Approved, MappingVersion = 2 }));

    [Fact]
    public void AnUndecidedProposalHasNoLiveVersion() =>
        Assert.Null(MappingGovernanceService.LiveVersionOf(Pending()));
}

/// <summary>S13.101 tests for the relaxed proposals query (ADR-PA34).</summary>
public sealed class MappingProposalQueryTests
{
    private const string OtherRole = "fast-extraction";

    private static FakeMappingProposalStore TwoRoles() => new FakeMappingProposalStore()
        .WithProposal(Pending("p-1"))
        .WithProposal(Pending("p-2") with { RoleId = OtherRole, ProposedAtUtc = FixedTimeProvider.Now.AddMinutes(-5) })
        .WithProposal(Pending("p-3") with { State = MappingProposalState.Rejected, ProposedAtUtc = FixedTimeProvider.Now.AddMinutes(-10) });

    [Fact]
    public async Task WithNoRole_ReturnsProposalsAcrossEveryRole()
    {
        // D3's headline view, which no per-role query can answer.
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder())
            .ListProposalsAsync(new MappingProposalQuery(), CancellationToken.None);

        Assert.Equal(3, page.Proposals.Count);
        Assert.Contains(page.Proposals, proposal => proposal.RoleId == OtherRole);
    }

    [Fact]
    public async Task WithARole_StaysWithinThatRole()
    {
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder())
            .ListProposalsAsync(new MappingProposalQuery { RoleId = OtherRole }, CancellationToken.None);

        Assert.Equal(OtherRole, Assert.Single(page.Proposals).RoleId);
    }

    [Fact]
    public async Task WithSeveralStates_ReturnsEveryMatchingState()
    {
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder()).ListProposalsAsync(
            new MappingProposalQuery { States = [MappingProposalState.PendingApproval, MappingProposalState.Rejected] },
            CancellationToken.None);

        Assert.Equal(3, page.Proposals.Count);
    }

    [Fact]
    public async Task WithOneState_FiltersToIt()
    {
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder()).ListProposalsAsync(
            new MappingProposalQuery { States = [MappingProposalState.Rejected] }, CancellationToken.None);

        Assert.Equal("p-3", Assert.Single(page.Proposals).ProposalId);
    }

    [Fact]
    public async Task WithNoStates_ReturnsEveryState()
    {
        // There is deliberately no default filter: "everything" stays expressible (ADR-PA34).
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder())
            .ListProposalsAsync(new MappingProposalQuery { States = [] }, CancellationToken.None);

        Assert.Equal(3, page.Proposals.Count);
    }

    [Fact]
    public async Task PagesThroughWithAContinuationToken()
    {
        var service = Service(TwoRoles(), new FakeMappingDecisionRecorder());

        var first = await service.ListProposalsAsync(new MappingProposalQuery { PageSize = 2 }, CancellationToken.None);
        var second = await service.ListProposalsAsync(
            new MappingProposalQuery { PageSize = 2, ContinuationToken = first.ContinuationToken }, CancellationToken.None);

        Assert.Equal(2, first.Proposals.Count);
        Assert.NotNull(first.ContinuationToken);
        Assert.Single(second.Proposals);
        Assert.Null(second.ContinuationToken);
        Assert.Empty(first.Proposals.Select(p => p.ProposalId).Intersect(second.Proposals.Select(p => p.ProposalId)));
    }

    [Fact]
    public async Task EveryListedProposalCarriesItsToken()
    {
        var page = await Service(TwoRoles(), new FakeMappingDecisionRecorder())
            .ListProposalsAsync(new MappingProposalQuery(), CancellationToken.None);

        Assert.All(page.Proposals, proposal => Assert.False(string.IsNullOrWhiteSpace(proposal.ConcurrencyToken)));
    }
}

/// <summary>S13.101 tests for <see cref="IMappingVersionHistory"/> (ADR-PA34).</summary>
public sealed class MappingVersionHistoryTests
{
    private static readonly RoleMapping CanaryV2 =
        FleetV1 with { MappingVersion = 2, Ring = RolloutRing.Canary, CanaryPercent = 10, PredecessorFleetVersion = 1, ChangeReason = "fable 5 eval", ApprovedBy = Approver };

    [Fact]
    public async Task ReturnsEveryVersionAscendingAndMarksTheCurrentOne()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1).WithVersion(CanaryV2, current: true);

        var history = await new MappingVersionHistory(store).GetVersionHistoryAsync(RoleId, CancellationToken.None);

        Assert.Equal([1, 2], history.Select(row => row.MappingVersion));
        Assert.False(history[0].IsCurrent);
        Assert.True(history[1].IsCurrent);
    }

    [Fact]
    public async Task CarriesEveryFieldD3Renders()
    {
        var store = new FakeMappingProposalStore().WithVersion(CanaryV2, current: true);

        var row = Assert.Single(await new MappingVersionHistory(store).GetVersionHistoryAsync(RoleId, CancellationToken.None));

        Assert.Equal(RolloutRing.Canary, row.Ring);
        Assert.Equal(10, row.CanaryPercent);
        Assert.Equal("fable 5 eval", row.ChangeReason);
        Assert.Equal(Approver, row.ApprovedBy);
        Assert.Equal(CanaryV2.EffectiveFromUtc, row.EffectiveFromUtc);
        Assert.Equal(CanaryV2.Chain, row.Chain);
    }

    [Fact]
    public async Task ARoleWithNoVersions_HasAnEmptyHistory() =>
        Assert.Empty(await new MappingVersionHistory(new FakeMappingProposalStore()).GetVersionHistoryAsync(RoleId, CancellationToken.None));

    [Fact]
    public async Task AVersionRolledAwayFrom_StaysInTheHistory()
    {
        // The history is evidence of what served; nothing is removed when a rollback moves the pointer.
        var store = new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithVersion(CanaryV2);

        var history = await new MappingVersionHistory(store).GetVersionHistoryAsync(RoleId, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].IsCurrent);
        Assert.False(history[1].IsCurrent);
    }

    [Fact]
    public async Task WithNoCurrentPointer_NoRowIsCurrent()
    {
        var store = new FakeMappingProposalStore().WithVersion(FleetV1);

        var history = await new MappingVersionHistory(store).GetVersionHistoryAsync(RoleId, CancellationToken.None);

        Assert.All(history, row => Assert.False(row.IsCurrent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankRole_Throws(string roleId) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new MappingVersionHistory(new FakeMappingProposalStore()).GetVersionHistoryAsync(roleId, CancellationToken.None));
}

/// <summary>
/// S13.101 tests that every governance refusal is catchable as its own type (ADR-PA34) — and still
/// as <see cref="ContractViolationException"/>, which is what keeps it a permanent failure.
/// </summary>
public sealed class MappingGovernanceRefusalTests
{
    [Fact]
    public async Task AnIllFormedChange_ThrowsInvalidMappingChange() =>
        await AssertRefusalAsync<InvalidMappingChangeException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .ProposeAsync(Change() with { ProposedMapping = Change().ProposedMapping with { CanaryPercent = 0 } }, Proposer, CancellationToken.None));

    [Fact]
    public async Task AnUnknownProposal_ThrowsUnknownMappingProposal() =>
        await AssertRefusalAsync<UnknownMappingProposalException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
                .ApproveAsync(RoleId, "missing", Approver, "reason", null, CancellationToken.None));

    [Fact]
    public async Task AnUnknownRollbackTarget_ThrowsUnknownMappingVersion() =>
        await AssertRefusalAsync<UnknownMappingVersionException>(() =>
            Service(new FakeMappingProposalStore().WithVersion(FleetV1, current: true), new FakeMappingDecisionRecorder())
                .RollbackToVersionAsync(RoleId, 99, Approver, "revert", CancellationToken.None));

    [Fact]
    public async Task ARoleWithNoCurrentPointer_ThrowsUnknownMappingVersion() =>
        await AssertRefusalAsync<UnknownMappingVersionException>(() =>
            Service(new FakeMappingProposalStore().WithVersion(FleetV1), new FakeMappingDecisionRecorder())
                .RollbackToVersionAsync(RoleId, 1, Approver, "revert", CancellationToken.None));

    [Fact]
    public async Task AShadowRollbackTarget_ThrowsNotRollbackEligible() =>
        await AssertRefusalAsync<MappingVersionNotRollbackEligibleException>(() =>
            Service(new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithVersion(ShadowV2), new FakeMappingDecisionRecorder())
                .RollbackToVersionAsync(RoleId, 2, Approver, "revert", CancellationToken.None));

    [Fact]
    public async Task AnIllegalTransition_ThrowsIllegalMappingTransition() =>
        await AssertRefusalAsync<IllegalMappingTransitionException>(() =>
            Service(new FakeMappingProposalStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .PromoteAsync(RoleId, "p-1", Approver, "too early", null, CancellationToken.None));

    [Fact]
    public async Task SelfApproval_ThrowsDistinctApproverRequired() =>
        await AssertRefusalAsync<DistinctApproverRequiredException>(() =>
            Service(new FakeMappingProposalStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .ApproveAsync(RoleId, "p-1", Proposer, "mine", null, CancellationToken.None));

    [Fact]
    public async Task WithdrawalByAnother_ThrowsProposerOnlyWithdrawal() =>
        await AssertRefusalAsync<ProposerOnlyWithdrawalException>(() =>
            Service(new FakeMappingProposalStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .WithdrawAsync(RoleId, "p-1", Approver, "not mine", null, CancellationToken.None));

    [Fact]
    public async Task ASecondProposal_ThrowsAlreadyPending() =>
        await AssertRefusalAsync<MappingProposalAlreadyPendingException>(() =>
            Service(new FakeMappingProposalStore().WithVersion(FleetV1, current: true).WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .ProposeAsync(Change(), Approver, CancellationToken.None));

    [Fact]
    public async Task AStaleToken_ThrowsMappingConcurrencyConflict() =>
        await AssertRefusalAsync<MappingConcurrencyConflictException>(() =>
            Service(new FakeMappingProposalStore().WithProposal(Pending()), new FakeMappingDecisionRecorder())
                .RejectAsync(RoleId, "p-1", Approver, "no", "etag-stale", CancellationToken.None));

    /// <summary>
    /// Every refusal is catchable as its own type, as the abstract refusal base, and as
    /// <see cref="ContractViolationException"/> — the last is what keeps it permanent and never
    /// retried, by <c>FailureClassifier</c> and by DTF's <c>IsCausedBy</c> alike.
    /// </summary>
    private static async Task AssertRefusalAsync<TRefusal>(Func<Task> act)
        where TRefusal : MappingGovernanceRefusalException
    {
        var refusal = await Assert.ThrowsAsync<TRefusal>(act);

        Assert.IsAssignableFrom<MappingGovernanceRefusalException>(refusal);
        Assert.IsAssignableFrom<ContractViolationException>(refusal);
        Assert.NotEmpty(refusal.Violations);
        Assert.False(string.IsNullOrWhiteSpace(refusal.ContractType));
    }
}

/// <summary>
/// S13.101 ADR-E15 checks for the ADR-PA34 additions: every new member is optional, so bytes written
/// before it read back without it and nothing downstream may require it.
/// </summary>
public sealed class MappingGovernanceSurfaceCompatibilityTests
{
    [Fact]
    public void AStoredProposalRecordedBeforeTheTokenExisted_ReadsBackWithItNull()
    {
        // The compatibility surface is the stored document, not the domain record: a proposal's chain
        // is an abstract ChainEntry, which is why MappingProposalDocument owns the wire shape
        // (ADR-PA27). These are real canonical bytes with no _etag - exactly a pre-ADR-PA34 document.
        var json = JsonSerializer.Serialize(MappingProposalDocument.FromDomain(Pending()), CanonicalProfile.Options);
        Assert.DoesNotContain("_etag", json, StringComparison.Ordinal);

        var document = JsonSerializer.Deserialize<MappingProposalDocument>(json, CanonicalProfile.Options)!;

        Assert.Null(document.ETag);
        Assert.Null(document.ToDomain().ConcurrencyToken);
    }

    [Fact]
    public void AStoredProposalCarryingCosmosEtag_SurfacesItAsTheConcurrencyToken()
    {
        // The read path: Cosmos returns _etag in the document body, and that is what a decision guards on.
        var node = JsonNode.Parse(JsonSerializer.Serialize(MappingProposalDocument.FromDomain(Pending()), CanonicalProfile.Options))!;
        node["_etag"] = "\"0x8DC\"";

        var document = JsonSerializer.Deserialize<MappingProposalDocument>(node.ToJsonString(), CanonicalProfile.Options)!;

        Assert.Equal("\"0x8DC\"", document.ETag);
    }

    [Fact]
    public void TheTokenIsNeverWrittenIntoTheStoredDocument()
    {
        // It is a property of the document, not of the proposal: stored bytes are unchanged (ADR-PA34).
        var document = MappingProposalDocument.FromDomain(Pending() with { ConcurrencyToken = "etag-7" });

        var json = JsonSerializer.Serialize(document, CanonicalProfile.Options);

        Assert.Null(document.ETag);
        Assert.DoesNotContain("etag-7", json, StringComparison.Ordinal);
        Assert.DoesNotContain("_etag", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AQueryConstructedWithNothingSet_IsAValidCrossRoleQuery()
    {
        var query = new MappingProposalQuery();

        Assert.Null(query.RoleId);
        Assert.Null(query.States);
        Assert.Equal(50, query.PageSize);
        Assert.Null(query.ContinuationToken);
    }

    [Fact]
    public void AProposalRoundTripsThroughItsDocumentUnchanged()
    {
        var proposal = Pending();

        var restored = MappingProposalDocument.FromDomain(proposal).ToDomain();

        Assert.Equal(proposal.ProposalId, restored.ProposalId);
        Assert.Equal(proposal.State, restored.State);
        Assert.Equal(proposal.ProposedBy, restored.ProposedBy);
        Assert.Null(restored.ConcurrencyToken);
    }
}
