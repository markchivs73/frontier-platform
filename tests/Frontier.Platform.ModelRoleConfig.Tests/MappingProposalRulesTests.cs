using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.MappingProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>S13.101 tests for <see cref="MappingProposalRules"/> (ADR-PA32): what may be proposed, and what a decided proposal must carry.</summary>
public sealed class MappingProposalRulesTests
{
    private static readonly AgentEntry Agent = new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/echo",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    [Fact]
    public void ValidateChange_WellFormed_DoesNotThrow() =>
        MappingProposalRules.ValidateChange(Change());

    [Fact]
    public void ValidateChange_EmptyChain_IsRefused()
    {
        var change = Change() with { ProposedMapping = FleetV1 with { Chain = [] } };

        var exception = Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));

        Assert.Contains("at least a primary entry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateChange_MixedChain_IsRefused()
    {
        // ADR-PA27's guard applies at propose time too, so an ill-shaped chain is never stored.
        var change = Change() with { ProposedMapping = FleetV1 with { Chain = [FleetV1.Chain[0], Agent] } };

        Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));
    }

    [Fact]
    public void ValidateChange_UnresolvableModelId_IsRefused()
    {
        // K7: a chain entry naming nothing the resolver could serve is refused at propose time
        // rather than discovered at the first invocation.
        var blank = (ModelEntry)FleetV1.Chain[0] with { ModelId = "  " };
        var change = Change() with { ProposedMapping = FleetV1 with { Chain = [blank] } };

        var exception = Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));

        Assert.Contains("names no target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateChange_RoleMismatchBetweenChangeAndMapping_IsRefused()
    {
        var change = Change() with { ProposedMapping = FleetV1 with { RoleId = "fast" } };

        Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateChange_CanaryPercentOutOfRange_IsRefused(int percent)
    {
        var change = Change() with { ProposedMapping = FleetV1 with { CanaryPercent = percent } };

        Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));
    }

    [Fact]
    public void ValidateChange_BlankReason_IsRefused()
    {
        // Reached directly rather than through ProposeAsync, whose argument guard throws first: a
        // remap without a reason is a config edit, which doc 08 §2 principle 3 forbids.
        var exception = Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(Change() with { Reason = " " }));

        Assert.Contains("reason is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateChange_BlankRole_IsRefused()
    {
        var change = Change() with { RoleId = " ", ProposedMapping = FleetV1 with { RoleId = " " } };

        Assert.ThrowsAny<ContractViolationException>(() => MappingProposalRules.ValidateChange(change));
    }

    [Fact]
    public void Validate_WellFormedPendingProposal_DoesNotThrow() =>
        Pending().Validate();

    [Theory]
    [InlineData("", "role", "proposer")]
    [InlineData("p-1", "", "proposer")]
    [InlineData("p-1", "role", "")]
    public void Validate_MissingIdentity_IsRefused(string proposalId, string roleId, string proposedBy)
    {
        var proposal = Pending() with { ProposalId = proposalId, RoleId = roleId, ProposedBy = proposedBy };

        Assert.ThrowsAny<ContractViolationException>(proposal.Validate);
    }

    [Fact]
    public void Validate_ApprovedWithoutItsVersionOrApprover_IsRefused()
    {
        var approved = Pending() with { State = MappingProposalState.Approved };

        var exception = Assert.ThrowsAny<ContractViolationException>(approved.Validate);

        Assert.Contains("mapping_version", exception.Message, StringComparison.Ordinal);
        Assert.Contains("approver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PromotedWithoutItsFleetVersion_IsRefused()
    {
        var promoted = Pending() with { State = MappingProposalState.Promoted };

        Assert.ThrowsAny<ContractViolationException>(promoted.Validate);
    }

    [Fact]
    public void Validate_RolledBackWithoutItsTarget_IsRefused()
    {
        var rolledBack = Pending() with { State = MappingProposalState.RolledBack };

        Assert.ThrowsAny<ContractViolationException>(rolledBack.Validate);
    }

    [Fact]
    public void EnsureDistinctApprover_IsCaseInsensitive() =>
        Assert.ThrowsAny<ContractViolationException>(() =>
            MappingProposalRules.EnsureDistinctApprover(Pending(), Proposer.ToUpperInvariant()));

    [Fact]
    public void EnsureDistinctApprover_ADifferentPrincipal_IsAllowed() =>
        MappingProposalRules.EnsureDistinctApprover(Pending(), Approver);
}
