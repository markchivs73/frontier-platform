using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionProposalSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="RoleDefinitionProposalRules"/> and <see cref="ContractRefFormat"/>: pure, so every branch is pinned here.</summary>
public sealed class RoleDefinitionProposalRulesTests
{
    [Theory]
    [InlineData("genealogy/record-pass-input/1.0", true)]
    [InlineData("acme.sub/in-put/12.30", true)]
    [InlineData("RecordPassInput", false)]
    [InlineData("genealogy/record-pass-input/1", false)]
    [InlineData("Genealogy/record/1.0", false)]
    [InlineData("genealogy/record/1.0/extra", false)]
    [InlineData("", false)]
    public void ContractRefFormat_AcceptsOnlyAdrE2SchemaRefs(string contractRef, bool valid) =>
        Assert.Equal(valid, ContractRefFormat.IsValid(contractRef));

    [Fact]
    public void ValidateDefinition_WellFormedAgentAndModelRoles_Pass()
    {
        RoleDefinitionProposalRules.ValidateDefinition(AgentDefinition());
        RoleDefinitionProposalRules.ValidateDefinition(ModelDefinition());
    }

    [Fact]
    public void ValidateDefinition_ListsEveryViolationAtOnce()
    {
        var bad = ModelDefinition() with
        {
            RoleId = "Deep:Reasoning",
            Description = " ",
            Requirements = null,
            InputContractRef = "not-a-ref",
            OutputContractRef = "also bad",
        };

        var refusal = Assert.Throws<InvalidRoleDefinitionException>(() => RoleDefinitionProposalRules.ValidateDefinition(bad));

        Assert.Equal(5, refusal.Violations.Count);
        Assert.Contains(refusal.Violations, violation => violation.StartsWith("input_contract_ref 'not-a-ref'", StringComparison.Ordinal));
        Assert.Contains(refusal.Violations, violation => violation.Contains("{namespace}/{name}/{major}.{minor}", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureBackingUnchanged_NewRoleOrSameBacking_Passes()
    {
        RoleDefinitionProposalRules.EnsureBackingUnchanged(null, AgentDefinition());
        RoleDefinitionProposalRules.EnsureBackingUnchanged(AgentDefinition(), AgentDefinition());
    }

    [Fact]
    public void EnsureBackingUnchanged_DifferentBacking_IsRefused()
    {
        var refusal = Assert.Throws<RoleBackingChangeException>(() =>
            RoleDefinitionProposalRules.EnsureBackingUnchanged(ModelDefinition(), AgentDefinition() with { RoleId = "deep-reasoning" }));

        Assert.Contains("is model-backed and its backing cannot change to agent", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProposal_PendingAndApproved_Pass()
    {
        RoleDefinitionProposalRules.ValidateProposal(Pending());
        RoleDefinitionProposalRules.ValidateProposal(RoleDefinitionProposalDocumentTests.Approved);
    }

    public static TheoryData<string, string> IllFormedProposals => new()
    {
        { "no-id", "proposal_id is required." },
        { "no-proposer", "proposed_by is required" },
        { "no-reason", "reason is required" },
        { "role-mismatch", "but the proposal names 'other-role'" },
        { "shadow", "never enters 'shadow'" },
        { "promoted", "never enters 'promoted'" },
        { "rolled-back", "never enters 'rolled_back'" },
        { "approved-unversioned", "must carry the definition_version" },
        { "approved-unattributed", "must carry the definition_version" },
    };

    [Theory]
    [MemberData(nameof(IllFormedProposals))]
    public void ValidateProposal_IllFormed_IsRefused(string shape, string expected)
    {
        var approved = RoleDefinitionProposalDocumentTests.Approved;
        var proposal = shape switch
        {
            "no-id" => Pending() with { ProposalId = " " },
            "no-proposer" => Pending() with { ProposedBy = "" },
            "no-reason" => Pending() with { Reason = " " },
            "role-mismatch" => Pending() with { RoleId = "other-role" },
            "shadow" => Pending() with { State = MappingProposalState.Shadow },
            "promoted" => Pending() with { State = MappingProposalState.Promoted },
            "rolled-back" => Pending() with { State = MappingProposalState.RolledBack },
            "approved-unversioned" => approved with { DefinitionVersion = null },
            _ => approved with { ApprovedBy = " " },
        };

        var refusal = Assert.Throws<InvalidRoleDefinitionException>(() => RoleDefinitionProposalRules.ValidateProposal(proposal));

        Assert.Contains(refusal.Violations, violation => violation.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Unstamped_ClearsEveryApprovalStamp_AndKeepsEverythingElse()
    {
        var stamped = RoleDefinitionSamples.AgentBacked(AgentRoleId);

        Assert.Equal(AgentDefinition(), RoleDefinitionProposalRules.Unstamped(stamped));
    }

    [Fact]
    public void ModelRoleConfigDocumentId_ForDefinitionProposal_UsesItsOwnInfix()
    {
        var id = ModelRoleConfigDocumentId.ForDefinitionProposal("fast", "abc");

        Assert.Equal("fast:definition-proposal:abc", id);
        Assert.False(id.StartsWith(ModelRoleConfigDocumentId.VersionPrefix("fast"), StringComparison.Ordinal));
        Assert.False(id.StartsWith("fast:proposal:", StringComparison.Ordinal));
    }

    [Fact]
    public void MappingProposalQueryBuilder_ForDefinitionProposals_SelectsTheirDocType()
    {
        var definition = MappingProposalQueryBuilder.Build(new MappingProposalQuery(), RoleDefinitionProposalDocument.ProposalDocType);

        Assert.Equal(("@docType", (object)"role_definition_proposal"), Assert.Single(definition.GetQueryParameters()));
    }

    [Fact]
    public void RoleDefinitionGovernanceEventTypes_AreTheFrozenS48Names()
    {
        Assert.Equal(
            ["model_role_definition_proposed", "model_role_definition_approved", "model_role_definition_rejected", "model_role_definition_withdrawn"],
            [RoleDefinitionGovernanceEventTypes.Proposed, RoleDefinitionGovernanceEventTypes.Approved, RoleDefinitionGovernanceEventTypes.Rejected, RoleDefinitionGovernanceEventTypes.Withdrawn]);
    }
}
