using static Frontier.Platform.ModelRoleConfig.Tests.EvaluationEvidenceSamples;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>
/// ADR-PA40 tests for <see cref="MappingGovernanceService"/> against role definitions (ADR-E27 D12,
/// D16): a mapping proposal needs a defined role whose backing is its chain's kind, an agent-backed
/// role's proposal needs evidence, the proposal stamps the role's contract hashes, and approval or
/// promotion refuses evidence the role's contracts have moved past. Every refusal writes nothing.
/// </summary>
public sealed class MappingEvidenceGovernanceTests
{
    [Fact]
    public async Task ProposeAsync_AgentRoleWithoutEvidence_IsRefusedAndNothingIsRecorded()
    {
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<EvaluationEvidenceRequiredException>(() =>
            Service(new FakeMappingProposalStore(), recorder, definitions: Defining(AgentRole))
                .ProposeAsync(AgentChange(evidenceRef: null), Proposer, CancellationToken.None));

        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_AgentRoleWithMalformedEvidence_IsRefused() =>
        await Assert.ThrowsAsync<EvaluationEvidenceRequiredException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder(), definitions: Defining(AgentRole))
                .ProposeAsync(AgentChange("run 42"), Proposer, CancellationToken.None));

    [Fact]
    public async Task ProposeAsync_AgentRoleWithManualEvidence_StampsTheRolesContractHashes()
    {
        // The caller's hashes are never trusted: a stamped field comes from the registry (ADR-PA32).
        var change = AgentChange() with { ProposedMapping = AgentMapping() with { InputContractHash = "sha256:forged" } };

        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder(), definitions: Defining(AgentRole))
            .ProposeAsync(change, Proposer, CancellationToken.None);

        Assert.Equal(ManualRef, proposal.Change.ProposedMapping.EvaluationEvidenceRef);
        Assert.Equal(AgentRole.InputContractHash, proposal.Change.ProposedMapping.InputContractHash);
        Assert.Equal(AgentRole.OutputContractHash, proposal.Change.ProposedMapping.OutputContractHash);
    }

    [Fact]
    public async Task ProposeAsync_AgentChainForAModelBackedRole_IsRefused()
    {
        var change = AgentChange() with { RoleId = RoleId, ProposedMapping = AgentMapping() with { RoleId = RoleId } };
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<ChainBackingMismatchException>(() =>
            Service(new FakeMappingProposalStore(), recorder).ProposeAsync(change, Proposer, CancellationToken.None));

        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_ModelChainForAnAgentBackedRole_IsRefused()
    {
        var change = Change() with { RoleId = AgentRole.RoleId, ProposedMapping = Change().ProposedMapping with { RoleId = AgentRole.RoleId, EvaluationEvidenceRef = ManualRef } };

        await Assert.ThrowsAsync<ChainBackingMismatchException>(() =>
            Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder(), definitions: Defining(AgentRole))
                .ProposeAsync(change, Proposer, CancellationToken.None));
    }

    [Fact]
    public async Task ProposeAsync_RoleWithNoDefinition_IsRefusedAsUnknownAndNothingIsRecorded()
    {
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<UnknownRoleDefinitionException>(() =>
            Service(new FakeMappingProposalStore(), recorder, definitions: new FakeRoleDefinitionStore())
                .ProposeAsync(Change(), Proposer, CancellationToken.None));

        Assert.Empty(recorder.Decisions);
    }

    [Fact]
    public async Task ProposeAsync_ModelRoleWithoutEvidence_IsStillAccepted()
    {
        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder())
            .ProposeAsync(Change(), Proposer, CancellationToken.None);

        Assert.Equal(MappingProposalState.PendingApproval, proposal.State);
        Assert.Null(proposal.Change.ProposedMapping.EvaluationEvidenceRef);
    }

    [Fact]
    public async Task ProposeAsync_ModelRoleWithEvidence_RecordsItAndStampsTheRolesHashes()
    {
        var role = RoleDefinitionSamples.Migrated(RoleDefinitionSamples.Phase1Role(RoleId)) with
        {
            OutputContractRef = "common/answer/1.0",
            OutputContractHash = "sha256:" + new string('f', 64),
        };
        var change = Change() with { ProposedMapping = Change().ProposedMapping with { EvaluationEvidenceRef = KitRef } };

        var proposal = await Service(new FakeMappingProposalStore(), new FakeMappingDecisionRecorder(), definitions: Defining(role))
            .ProposeAsync(change, Proposer, CancellationToken.None);

        Assert.Equal(KitRef, proposal.Change.ProposedMapping.EvaluationEvidenceRef);
        Assert.Null(proposal.Change.ProposedMapping.InputContractHash);
        Assert.Equal(role.OutputContractHash, proposal.Change.ProposedMapping.OutputContractHash);
    }

    [Fact]
    public async Task ApproveAsync_CopiesTheEvidenceAndHashesOntoTheMintedVersion()
    {
        var store = new FakeMappingProposalStore().WithProposal(AgentPending(AgentRole));

        var approved = await Service(store, new FakeMappingDecisionRecorder(), definitions: Defining(AgentRole))
            .ApproveAsync(AgentRole.RoleId, "p-agent", Approver, "evidence reviewed", null, CancellationToken.None);

        var minted = store.Version(approved.MappingVersion!.Value)!;
        Assert.Equal(ManualRef, minted.EvaluationEvidenceRef);
        Assert.Equal(AgentRole.InputContractHash, minted.InputContractHash);
        Assert.Equal(AgentRole.OutputContractHash, minted.OutputContractHash);
    }

    [Fact]
    public async Task ApproveAsync_RoleContractMovedSincePropose_IsRefusedAsStaleAndNothingIsWritten()
    {
        var store = new FakeMappingProposalStore().WithProposal(AgentPending(AgentRole));
        var recorder = new FakeMappingDecisionRecorder();

        await Assert.ThrowsAsync<EvaluationEvidenceStaleException>(() =>
            Service(store, recorder, definitions: Defining(AgentRoleWithNewOutput))
                .ApproveAsync(AgentRole.RoleId, "p-agent", Approver, "evidence reviewed", null, CancellationToken.None));

        Assert.Empty(recorder.Decisions);
        Assert.Null(store.Version(1));
        Assert.Equal(MappingProposalState.PendingApproval, store.Proposal("p-agent")!.State);
    }

    [Fact]
    public async Task PromoteAsync_RoleContractMovedSinceApproval_IsRefusedAsStale()
    {
        var approved = AgentPending(AgentRole) with
        {
            State = MappingProposalState.Approved,
            MappingVersion = 1,
            ApprovedBy = Approver,
            EffectiveFromUtc = FixedTimeProvider.Now,
        };
        var store = new FakeMappingProposalStore().WithProposal(approved);

        await Assert.ThrowsAsync<EvaluationEvidenceStaleException>(() =>
            Service(store, new FakeMappingDecisionRecorder(), definitions: Defining(AgentRoleWithNewOutput))
                .PromoteAsync(AgentRole.RoleId, "p-agent", Approver, "clean week", null, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_PendingAgentChainOnAModelBackedRole_IsRefusedAtApproval()
    {
        // A proposal raised before v0.45.0 was never checked against a backing; approval checks it.
        var legacy = AgentPending(AgentRole) with { RoleId = RoleId, Change = AgentChange(evidenceRef: null) with { RoleId = RoleId } };
        var store = new FakeMappingProposalStore().WithProposal(legacy);

        await Assert.ThrowsAsync<ChainBackingMismatchException>(() =>
            Service(store, new FakeMappingDecisionRecorder())
                .ApproveAsync(RoleId, "p-agent", Approver, "legacy", null, CancellationToken.None));
    }

    [Fact]
    public async Task ApproveAsync_RoleNoLongerDefined_IsRefusedAsUnknown()
    {
        var store = new FakeMappingProposalStore().WithProposal(Pending());

        await Assert.ThrowsAsync<UnknownRoleDefinitionException>(() =>
            Service(store, new FakeMappingDecisionRecorder(), definitions: new FakeRoleDefinitionStore())
                .ApproveAsync(RoleId, "p-1", Approver, "eval accepted", null, CancellationToken.None));
    }

    /// <summary>A pending agent-role proposal as the service would have stamped it against <paramref name="role"/>.</summary>
    private static MappingChangeProposal AgentPending(RoleDefinition role) => new()
    {
        ProposalId = "p-agent",
        RoleId = role.RoleId,
        State = MappingProposalState.PendingApproval,
        ProposedBy = Proposer,
        ProposedAtUtc = FixedTimeProvider.Now,
        Change = AgentChange() with { ProposedMapping = MappingEligibilityRules.Stamp(AgentMapping(), role) },
    };
}
