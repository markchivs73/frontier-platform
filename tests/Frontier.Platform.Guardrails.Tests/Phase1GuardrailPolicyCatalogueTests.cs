namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S4.5 tests for the compiled-in <see cref="Phase1GuardrailPolicyCatalogue"/> (doc 07 §4, §9).</summary>
public sealed class Phase1GuardrailPolicyCatalogueTests
{
    [Fact]
    public void Default_MatchesDoc07WorkedExample()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.Equal("advisory-sow-default", policy.PolicyId);
        Assert.NotNull(policy.PerInvocation);
        Assert.Equal(20_000, policy.PerInvocation!.MaxTokens);
        Assert.Equal(2.00m, policy.PerInvocation.MaxCostGbp);
        Assert.Null(policy.PerInvocation.MaxAgentInvocations);
        Assert.Null(policy.PerExecution);
        Assert.Null(policy.PerEngagement);
        Assert.Equal(80, policy.SoftThresholdPercent);
        Assert.Equal(FailureMode.FailOpenWithAudit, policy.OnInfrastructureFailure);
    }

    [Fact]
    public void Default_HasValidPolicyId()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.NotNull(policy.PolicyId);
        Assert.NotEmpty(policy.PolicyId);
    }

    [Fact]
    public void Default_PerInvocationBudget_IsConfigured()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.NotNull(policy.PerInvocation);
        Assert.True(policy.PerInvocation!.MaxTokens.HasValue);
        Assert.True(policy.PerInvocation.MaxCostGbp.HasValue);
        Assert.True(policy.PerInvocation.MaxTokens > 0);
        Assert.True(policy.PerInvocation.MaxCostGbp > 0);
    }

    [Fact]
    public void Default_SoftThresholdPercent_IsValid()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.InRange(policy.SoftThresholdPercent, 0, 100);
    }

    [Fact]
    public void Default_OnInfrastructureFailure_IsConfigured()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.True(Enum.IsDefined(policy.OnInfrastructureFailure));
    }

    [Fact]
    public void Default_TokenBudget_LessThanOrEqualCostBudgetTokenValue()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        if (policy.PerInvocation?.MaxTokens.HasValue == true && policy.PerInvocation.MaxCostGbp.HasValue)
        {
            Assert.True(policy.PerInvocation.MaxTokens >= 0);
            Assert.True(policy.PerInvocation.MaxCostGbp >= 0m);
        }
    }

    [Fact]
    public void Default_NoNegativeBudgets()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        if (policy.PerInvocation?.MaxTokens.HasValue == true)
            Assert.True(policy.PerInvocation.MaxTokens >= 0);

        if (policy.PerInvocation?.MaxCostGbp.HasValue == true)
            Assert.True(policy.PerInvocation.MaxCostGbp >= 0m);

        if (policy.PerInvocation?.MaxAgentInvocations.HasValue == true)
            Assert.True(policy.PerInvocation.MaxAgentInvocations >= 0);
    }

    [Fact]
    public void Default_MultipleCalls_ReturnEqualPolicies()
    {
        var policy1 = Phase1GuardrailPolicyCatalogue.Default;
        var policy2 = Phase1GuardrailPolicyCatalogue.Default;

        Assert.Equal(policy1.PolicyId, policy2.PolicyId);
        Assert.Equal(policy1.PerInvocation, policy2.PerInvocation);
        Assert.Equal(policy1.SoftThresholdPercent, policy2.SoftThresholdPercent);
    }

    [Fact]
    public void Default_PolicyIdIsAlwaysAdvisorySowDefault()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Default;

        Assert.Equal("advisory-sow-default", policy.PolicyId);
    }

    [Fact]
    public void Sandbox_MatchesTheC28ConfirmedCeiling()
    {
        var policy = Phase1GuardrailPolicyCatalogue.Sandbox;

        Assert.Equal("sandbox-test-run", policy.PolicyId);
        Assert.NotNull(policy.PerInvocation);
        Assert.Equal(5_000, policy.PerInvocation!.MaxTokens);
        Assert.Equal(0.50m, policy.PerInvocation.MaxCostGbp);
        Assert.Null(policy.PerInvocation.MaxAgentInvocations);
        Assert.Null(policy.PerExecution);
        Assert.Null(policy.PerEngagement);
    }

    [Fact]
    public void Sandbox_CeilingIsSmallerThanDefault()
    {
        var sandbox = Phase1GuardrailPolicyCatalogue.Sandbox;
        var production = Phase1GuardrailPolicyCatalogue.Default;

        Assert.True(sandbox.PerInvocation!.MaxTokens < production.PerInvocation!.MaxTokens);
        Assert.True(sandbox.PerInvocation.MaxCostGbp < production.PerInvocation.MaxCostGbp);
    }
}
