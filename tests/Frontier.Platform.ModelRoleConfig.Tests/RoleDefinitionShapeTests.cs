using Frontier.Platform.Abstractions;
using static Frontier.Platform.ModelRoleConfig.Tests.RoleDefinitionSamples;

namespace Frontier.Platform.ModelRoleConfig.Tests;

/// <summary>ADR-PA40 tests for <see cref="RoleDefinitionShape"/>: what any stored definition must look like.</summary>
public sealed class RoleDefinitionShapeTests
{
    [Fact]
    public void EnsureValid_EveryPhase1Role_IsReturnedUnchanged()
    {
        Assert.All(Phase1.Roles, role => Assert.Same(role, RoleDefinitionShape.EnsureValid(role)));
    }

    [Fact]
    public void EnsureValid_AgentBackedRoleWithBothRefsAndNoRequirements_IsValid()
    {
        var role = AgentBacked();

        Assert.Same(role, RoleDefinitionShape.EnsureValid(role));
    }

    [Fact]
    public void EnsureValid_ModelBackedRoleWithRefs_IsValid()
    {
        // ADR-E27 Q1: a model-backed role may declare either, both or none.
        var role = Phase1Role("fast") with { InputContractRef = "core/text/1.0" };

        Assert.Same(role, RoleDefinitionShape.EnsureValid(role));
    }

    [Fact]
    public void Violations_BadRoleId_IsNamed()
    {
        var violation = Assert.Single(RoleDefinitionShape.Violations(Phase1Role("fast") with { RoleId = "fast:v1" }));

        Assert.Contains("kebab-case", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Violations_ModelBackedRoleWithoutRequirements_IsNamed()
    {
        var violation = Assert.Single(RoleDefinitionShape.Violations(Phase1Role("fast") with { Requirements = null }));

        Assert.Contains("capability requirements", violation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "core/out/1.0")]
    [InlineData("core/in/1.0", null)]
    [InlineData(" ", "core/out/1.0")]
    [InlineData("core/in/1.0", "")]
    public void Violations_AgentBackedRoleMissingARef_IsNamed(string? input, string? output)
    {
        var violation = Assert.Single(RoleDefinitionShape.Violations(AgentBacked() with { InputContractRef = input, OutputContractRef = output }));

        Assert.Contains("both an input and an output contract ref", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureValid_EveryViolation_IsReportedTogetherAsAPermanentFailure()
    {
        var role = AgentBacked() with { RoleId = "Bad", InputContractRef = null };

        var ex = Assert.Throws<ContractViolationException>(() => RoleDefinitionShape.EnsureValid(role));

        Assert.Equal(2, ex.Violations.Count);
    }
}
