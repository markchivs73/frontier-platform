namespace Frontier.Platform.Guardrails.Tests;

/// <summary>Tests for contract validation on guardrails data records.</summary>
public sealed class ContractValidationTests
{
    [Fact]
    public void AdmissionDecision_ValidCase_CanBeConstructed()
    {
        var decision = new AdmissionDecision(
            AdmissionResult.Proceed,
            "Approved",
            50_000,
            null);

        Assert.Equal(AdmissionResult.Proceed, decision.Result);
        Assert.Equal("Approved", decision.Reason);
        Assert.Equal(50_000, decision.GrantedMaxOutputTokens);
        Assert.Null(decision.RetryAfter);
    }

    [Fact]
    public void AdmissionDecision_WithNullReason_IsValid()
    {
        var decision = new AdmissionDecision(AdmissionResult.Deny, null, null, null);

        Assert.Equal(AdmissionResult.Deny, decision.Result);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void AdmissionDecision_WithRetryAfter_IsValid()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var decision = new AdmissionDecision(
            AdmissionResult.ProceedWithWarning,
            "Rate limited",
            null,
            retryAfter);

        Assert.Equal(retryAfter, decision.RetryAfter);
    }

    [Fact]
    public void BudgetScopeRef_FleetScope_CanBeConstructed()
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Fleet, "ignored");

        Assert.Equal(BudgetScopeKind.Fleet, scope.Kind);
    }

    [Fact]
    public void BudgetScopeRef_ExecutionScope_PreservesId()
    {
        var executionId = "exec-123";
        var scope = new BudgetScopeRef(BudgetScopeKind.Execution, executionId);

        Assert.Equal(BudgetScopeKind.Execution, scope.Kind);
        Assert.Equal(executionId, scope.Id);
    }

    [Fact]
    public void BudgetScopeRef_EngagementScope_PreservesId()
    {
        var engagementId = "eng-456";
        var scope = new BudgetScopeRef(BudgetScopeKind.Engagement, engagementId);

        Assert.Equal(BudgetScopeKind.Engagement, scope.Kind);
        Assert.Equal(engagementId, scope.Id);
    }

    [Fact]
    public void BudgetSnapshot_ValidCase_CanBeConstructed()
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Fleet, "");
        var snapshot = new BudgetSnapshot(scope, 1000, 0.50m, 5);

        Assert.Equal(scope, snapshot.Scope);
        Assert.Equal(1000, snapshot.TokensUsed);
        Assert.Equal(0.50m, snapshot.CostGbp);
        Assert.Equal(5, snapshot.InvocationCount);
    }

    [Fact]
    public void BudgetSnapshot_ZeroUsage_IsValid()
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Invocation, "inv-1");
        var snapshot = new BudgetSnapshot(scope, 0, 0m, 0);

        Assert.Equal(0, snapshot.TokensUsed);
        Assert.Equal(0m, snapshot.CostGbp);
        Assert.Equal(0, snapshot.InvocationCount);
    }

    [Fact]
    public void BudgetSpec_AllLimits_CanBeSet()
    {
        var spec = new BudgetSpec(100_000, 1.00m, 50);

        Assert.Equal(100_000, spec.MaxTokens);
        Assert.Equal(1.00m, spec.MaxCostGbp);
        Assert.Equal(50, spec.MaxAgentInvocations);
    }

    [Fact]
    public void BudgetSpec_UnboundedLimits_AllowNull()
    {
        var spec = new BudgetSpec(null, null, null);

        Assert.Null(spec.MaxTokens);
        Assert.Null(spec.MaxCostGbp);
        Assert.Null(spec.MaxAgentInvocations);
    }

    [Fact]
    public void BudgetSpec_MixedLimits_CanBeCombined()
    {
        var spec = new BudgetSpec(50_000, null, 25);

        Assert.Equal(50_000, spec.MaxTokens);
        Assert.Null(spec.MaxCostGbp);
        Assert.Equal(25, spec.MaxAgentInvocations);
    }

    [Fact]
    public void InvocationCostEstimate_ValidCase_CanBeConstructed()
    {
        var estimate = new InvocationCostEstimate(
            "corr-123",
            "exec-456",
            "eng-789",
            "node-abc",
            "analyst",
            "claude-3-sonnet",
            5000,
            2000,
            0.30m);

        Assert.Equal("corr-123", estimate.CorrelationId);
        Assert.Equal("exec-456", estimate.ExecutionId);
        Assert.Equal("eng-789", estimate.EngagementId);
        Assert.Equal(5000, estimate.PromptTokens);
        Assert.Equal(2000, estimate.MaxOutputTokens);
        Assert.Equal(0.30m, estimate.EstimatedCostGbp);
    }

    [Fact]
    public void UsageRecord_ValidCase_CanBeConstructed()
    {
        var usage = new UsageRecord(
            "corr-789",
            "exec-012",
            "eng-345",
            "node-xyz",
            "agent",
            "claude-3-opus",
            4500,
            1500,
            0.25m);

        Assert.Equal("corr-789", usage.CorrelationId);
        Assert.Equal("exec-012", usage.ExecutionId);
        Assert.Equal(4500, usage.InputTokens);
        Assert.Equal(1500, usage.OutputTokens);
        Assert.Equal(0.25m, usage.CostGbp);
    }

    [Fact]
    public void BudgetLedgerDocument_ValidCase_CanBeConstructed()
    {
        var doc = new BudgetLedgerDocument
        {
            PartitionKey = "eng-123",
            Id = "eng-123:ledger",
            EngagementId = "eng-123",
            TotalInputTokens = 50_000,
            TotalOutputTokens = 25_000,
            TotalCostGbp = 5.00m,
            InvocationCount = 10
        };

        Assert.Equal("eng-123", doc.PartitionKey);
        Assert.Equal(50_000, doc.TotalInputTokens);
        Assert.Equal(25_000, doc.TotalOutputTokens);
        Assert.Equal(5.00m, doc.TotalCostGbp);
    }

    [Fact]
    public void ExecutionLedgerSnapshot_ValidCase_CanBeConstructed()
    {
        var snapshot = new ExecutionLedgerSnapshot(
            "exec-123",
            15_000,
            1.50m,
            3,
            DateTime.UtcNow);

        Assert.Equal("exec-123", snapshot.ExecutionId);
        Assert.Equal(15_000, snapshot.TotalTokens);
        Assert.Equal(1.50m, snapshot.TotalCostGbp);
        Assert.Equal(3, snapshot.InvocationCount);
    }
}
