using static Frontier.Platform.ModelRoleConfig.Tests.EvaluationEvidenceSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="MappingEligibilityRules"/>: a mapping against its role's backing, evidence and contract hashes (D16).</summary>
public sealed class MappingEligibilityRulesTests
{
    private static RoleDefinition ModelRole => RoleDefinitionSamples.Migrated(RoleDefinitionSamples.Phase1Role("deep-reasoning"));

    [Fact]
    public void EnsureDefined_NoDefinition_IsRefusedAsUnknown()
    {
        var refusal = Assert.Throws<UnknownRoleDefinitionException>(() => MappingEligibilityRules.EnsureDefined(null, "gen-record-pass"));

        Assert.Contains("gen-record-pass", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureDefined_Definition_ReturnsIt()
    {
        var role = AgentRole;

        Assert.Same(role, MappingEligibilityRules.EnsureDefined(role, role.RoleId));
    }

    [Fact]
    public void KindOf_AgentChain_IsAgent() =>
        Assert.Equal(RoleBacking.Agent, MappingEligibilityRules.KindOf([Agent]));

    [Fact]
    public void KindOf_ModelChain_IsModel() =>
        Assert.Equal(RoleBacking.Model, MappingEligibilityRules.KindOf(Phase1RoleCatalogue.DeepReasoningMappingV1.Chain));

    [Fact]
    public void EnsureEligible_AgentChainOnAModelBackedRole_IsRefused()
    {
        var mapping = AgentMapping() with { RoleId = ModelRole.RoleId };

        var refusal = Assert.Throws<ChainBackingMismatchException>(() => MappingEligibilityRules.EnsureEligible(ModelRole, mapping));

        Assert.Contains("model-backed", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureEligible_ModelChainOnAnAgentBackedRole_IsRefused()
    {
        var mapping = Phase1RoleCatalogue.DeepReasoningMappingV1 with { RoleId = AgentRole.RoleId, EvaluationEvidenceRef = ManualRef };

        Assert.Throws<ChainBackingMismatchException>(() => MappingEligibilityRules.EnsureEligible(AgentRole, mapping));
    }

    [Fact]
    public void EnsureEligible_AgentRoleWithoutEvidence_IsRefusedAsRequired()
    {
        var refusal = Assert.Throws<EvaluationEvidenceRequiredException>(() =>
            MappingEligibilityRules.EnsureEligible(AgentRole, AgentMapping(evidenceRef: null)));

        Assert.Contains("none was given", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureEligible_AgentRoleWithMalformedEvidence_IsRefusedAsRequired()
    {
        var refusal = Assert.Throws<EvaluationEvidenceRequiredException>(() =>
            MappingEligibilityRules.EnsureEligible(AgentRole, AgentMapping("https://evals.example/run-1")));

        Assert.Contains("is not of that form", Assert.Single(refusal.Violations), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ManualRef)]
    [InlineData(KitRef)]
    public void EnsureEligible_AgentRoleWithWellFormedEvidence_IsAccepted(string evidenceRef) =>
        MappingEligibilityRules.EnsureEligible(AgentRole, AgentMapping(evidenceRef));

    [Fact]
    public void EnsureEligible_ModelRoleWithoutEvidence_IsAccepted() =>
        MappingEligibilityRules.EnsureEligible(ModelRole, Phase1RoleCatalogue.DeepReasoningMappingV1);

    [Fact]
    public void EnsureEligible_ModelRoleWithWellFormedEvidence_IsAccepted() =>
        MappingEligibilityRules.EnsureEligible(ModelRole, Phase1RoleCatalogue.DeepReasoningMappingV1 with { EvaluationEvidenceRef = KitRef });

    [Fact]
    public void EnsureEligible_ModelRoleWithMalformedEvidence_IsRefused() =>
        // Optional is not free-form: a ref that is stored must parse when S13.137 resolves it.
        Assert.Throws<EvaluationEvidenceRequiredException>(() =>
            MappingEligibilityRules.EnsureEligible(ModelRole, Phase1RoleCatalogue.DeepReasoningMappingV1 with { EvaluationEvidenceRef = "see the wiki" }));

    [Fact]
    public void EnsureNotStale_NoEvidence_IsAccepted() =>
        MappingEligibilityRules.EnsureNotStale(AgentRoleWithNewOutput, AgentMapping(evidenceRef: null));

    [Fact]
    public void EnsureNotStale_EvidenceForTheCurrentHashes_IsAccepted() =>
        MappingEligibilityRules.EnsureNotStale(AgentRole, MappingEligibilityRules.Stamp(AgentMapping(), AgentRole));

    [Fact]
    public void EnsureNotStale_EvidenceForHashesTheRoleHasMovedPast_IsRefused()
    {
        var refusal = Assert.Throws<EvaluationEvidenceStaleException>(() =>
            MappingEligibilityRules.EnsureNotStale(AgentRoleWithNewOutput, MappingEligibilityRules.Stamp(AgentMapping(), AgentRole)));

        var violation = Assert.Single(refusal.Violations);
        Assert.Contains("definition v3", violation, StringComparison.Ordinal);
        Assert.Contains(ManualRef, violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Stamp_ReplacesTheCallersHashesWithTheRoles()
    {
        var forged = AgentMapping() with { InputContractHash = "sha256:forged", OutputContractHash = "sha256:forged" };

        var stamped = MappingEligibilityRules.Stamp(forged, AgentRole);

        Assert.Equal(AgentRole.InputContractHash, stamped.InputContractHash);
        Assert.Equal(AgentRole.OutputContractHash, stamped.OutputContractHash);
    }
}
