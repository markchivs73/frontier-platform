namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S4.5 tests for <see cref="AdmissionController"/> against doc 07 §4's admission rules.</summary>
public sealed class AdmissionControllerTests
{
    private readonly AdmissionController controller = new();

    [Fact]
    public async Task AdmitAsync_NullEstimate_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => controller.AdmitAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task AdmitAsync_WithinBudget_ReturnsProceedUnchanged()
    {
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500);

        var decision = await controller.AdmitAsync(estimate, CancellationToken.None);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Null(decision.Reason);
        Assert.Equal(500, decision.GrantedMaxOutputTokens);
        Assert.Null(decision.RetryAfter);
    }

    [Fact]
    public async Task AdmitAsync_RemainingBelowRequestedOutput_ReturnsProceedWithWarningAndShapedTokens()
    {
        var estimate = Estimate(promptTokens: 19_900, maxOutputTokens: 200);

        var decision = await controller.AdmitAsync(estimate, CancellationToken.None);

        Assert.Equal(AdmissionResult.ProceedWithWarning, decision.Result);
        Assert.Equal(100, decision.GrantedMaxOutputTokens);
        Assert.Contains("advisory-sow-default", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitAsync_PromptAlreadyExhaustsBudget_ReturnsDeny()
    {
        var estimate = Estimate(promptTokens: 20_000, maxOutputTokens: 500);

        var decision = await controller.AdmitAsync(estimate, CancellationToken.None);

        Assert.Equal(AdmissionResult.Deny, decision.Result);
        Assert.Null(decision.GrantedMaxOutputTokens);
        Assert.Null(decision.RetryAfter);
        Assert.Contains("advisory-sow-default", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Admit_PolicyWithoutPerInvocationBudget_ReturnsProceedUnchanged()
    {
        var policy = new GuardrailPolicy("unbounded", PerInvocation: null, PerExecution: null, PerEngagement: null);
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500);

        var decision = AdmissionController.Admit(estimate, policy);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Equal(500, decision.GrantedMaxOutputTokens);
    }

    [Fact]
    public void Admit_PerInvocationBudgetWithoutMaxTokens_ReturnsProceedUnchanged()
    {
        var policy = new GuardrailPolicy(
            "no-token-cap",
            PerInvocation: new BudgetSpec(MaxTokens: null, MaxCostGbp: 2.00m, MaxAgentInvocations: null),
            PerExecution: null,
            PerEngagement: null);
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500);

        var decision = AdmissionController.Admit(estimate, policy);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Equal(500, decision.GrantedMaxOutputTokens);
    }

    [Fact]
    public async Task AdmitAsync_SandboxExecutionId_UsesTheSmallerSandboxCeiling()
    {
        // 5,000 (Sandbox) < 3,000 prompt + 3,000 output < 20,000 (Default) — proves the
        // smaller ceiling is actually the one enforced, not just selected.
        var estimate = Estimate(promptTokens: 3_000, maxOutputTokens: 3_000) with { ExecutionId = "SANDBOX-abc123::wf-test" };

        var decision = await controller.AdmitAsync(estimate, CancellationToken.None);

        Assert.Equal(AdmissionResult.ProceedWithWarning, decision.Result);
        Assert.Equal(2_000, decision.GrantedMaxOutputTokens);
        Assert.Contains("sandbox-test-run", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitAsync_NonSandboxExecutionId_UsesTheDefaultCeiling()
    {
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500) with { ExecutionId = "engagement-1::wf-test" };

        var decision = await controller.AdmitAsync(estimate, CancellationToken.None);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Equal(500, decision.GrantedMaxOutputTokens);
    }

    [Theory]
    [InlineData("SANDBOX-abc::wf-test")]
    [InlineData("SANDBOX-")]
    public void SelectPolicy_SandboxPrefixedExecutionId_ReturnsSandboxPolicy(string executionId)
    {
        var policy = AdmissionController.SelectPolicy(executionId);

        Assert.Equal(Phase1GuardrailPolicyCatalogue.Sandbox.PolicyId, policy.PolicyId);
    }

    [Theory]
    [InlineData("engagement-1::wf-test")]
    [InlineData("")]
    public void SelectPolicy_NonSandboxExecutionId_ReturnsDefaultPolicy(string executionId)
    {
        var policy = AdmissionController.SelectPolicy(executionId);

        Assert.Equal(Phase1GuardrailPolicyCatalogue.Default.PolicyId, policy.PolicyId);
    }

    internal static InvocationCostEstimate Estimate(long promptTokens, long maxOutputTokens) => new(
        CorrelationId: "correlation-1",
        ExecutionId: "execution-1",
        EngagementId: "engagement-1",
        NodeId: "gen-pricing",
        AgentRole: "deep-reasoning",
        ResolvedModel: "claude-fable-5",
        PromptTokens: promptTokens,
        MaxOutputTokens: maxOutputTokens,
        EstimatedCostGbp: 0.10m);
}
