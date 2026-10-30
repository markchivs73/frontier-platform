using static Frontier.Platform.ModelRoleConfig.Tests.EvaluationEvidenceSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="EvaluationEvidence"/>: the ref grammar and the re-opened-eligibility signal D3 shows (S4-10).</summary>
public sealed class EvaluationEvidenceTests
{
    [Theory]
    [InlineData(ManualRef)]
    [InlineData(KitRef)]
    [InlineData("eval-run:kit:x")]
    public void IsWellFormed_ManualOrKitWithAnId_IsTrue(string evidenceRef) =>
        Assert.True(EvaluationEvidence.IsWellFormed(evidenceRef));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eval-run:manual:")]
    [InlineData("eval-run:kit:   ")]
    [InlineData("eval-run:kit")]
    [InlineData("eval-run:other:run-1")]
    [InlineData("EVAL-RUN:kit:run-1")]
    [InlineData("https://evals.example/run-1")]
    [InlineData(" eval-run:kit:run-1")]
    public void IsWellFormed_AnythingElse_IsFalse(string? evidenceRef) =>
        Assert.False(EvaluationEvidence.IsWellFormed(evidenceRef));

    [Fact]
    public void IsEligibilityReopened_AgentMappingProposedAgainstTheCurrentHashes_IsFalse() =>
        Assert.False(EvaluationEvidence.IsEligibilityReopened(AgentRole, Stamped(AgentRole)));

    [Fact]
    public void IsEligibilityReopened_OutputContractMovedSinceTheMapping_IsTrue() =>
        Assert.True(EvaluationEvidence.IsEligibilityReopened(AgentRoleWithNewOutput, Stamped(AgentRole)));

    [Fact]
    public void IsEligibilityReopened_InputContractMovedSinceTheMapping_IsTrue()
    {
        var moved = AgentRole with { InputContractHash = "sha256:" + new string('d', 64) };

        Assert.True(EvaluationEvidence.IsEligibilityReopened(moved, Stamped(AgentRole)));
    }

    [Fact]
    public void IsEligibilityReopened_AgentMappingWithNoStampedHashes_IsTrue() =>
        // A mapping stored before v0.45.0 carries no hashes, so no evidence covers the role's contracts.
        Assert.True(EvaluationEvidence.IsEligibilityReopened(AgentRole, AgentMapping(evidenceRef: null)));

    [Fact]
    public void IsEligibilityReopened_ModelBackedRole_IsAlwaysFalse()
    {
        // Evidence is optional for a model-backed role (S4-11), so a contract change re-opens nothing.
        var role = RoleDefinitionSamples.Migrated(RoleDefinitionSamples.Phase1Role("deep-reasoning")) with
        {
            InputContractRef = "common/question/1.0",
            InputContractHash = "sha256:" + new string('e', 64),
        };

        Assert.False(EvaluationEvidence.IsEligibilityReopened(role, Phase1RoleCatalogue.DeepReasoningMappingV1));
    }

    [Fact]
    public void IsEligibilityReopened_MappingForAnotherRole_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            EvaluationEvidence.IsEligibilityReopened(AgentRole, Phase1RoleCatalogue.DeepReasoningMappingV1));

    [Fact]
    public void IsEligibilityReopened_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => EvaluationEvidence.IsEligibilityReopened(null!, AgentMapping()));
        Assert.Throws<ArgumentNullException>(() => EvaluationEvidence.IsEligibilityReopened(AgentRole, null!));
    }

    private static RoleMapping Stamped(RoleDefinition role) => MappingEligibilityRules.Stamp(AgentMapping(), role);
}
