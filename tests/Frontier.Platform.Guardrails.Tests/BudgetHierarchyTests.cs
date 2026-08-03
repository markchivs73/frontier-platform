namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S6.5 tests for <see cref="BudgetHierarchy"/> (doc 07 §4 hierarchical budget enforcement).</summary>
public sealed class BudgetHierarchyTests
{
    private readonly BudgetLedger ledger = new();

    // ── CanApproveAsync guard-clause tests ────────────────────────────────────

    [Fact]
    public async Task CanApproveAsync_NullInvocationScope_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.CanApproveAsync(null!, Scope(BudgetScopeKind.Engagement, "eng-1"), Estimate(), CancellationToken.None));
    }

    [Fact]
    public async Task CanApproveAsync_NullEngagementScope_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.CanApproveAsync(Scope(BudgetScopeKind.Invocation, "corr-1"), null!, Estimate(), CancellationToken.None));
    }

    [Fact]
    public async Task CanApproveAsync_NullEstimate_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.CanApproveAsync(Scope(BudgetScopeKind.Invocation, "corr-1"), Scope(BudgetScopeKind.Engagement, "eng-1"), null!, CancellationToken.None));
    }

    // ── CanApproveAsync approval/denial tests ─────────────────────────────────

    [Fact]
    public async Task CanApproveAsync_AllBudgetsNull_ReturnsTrue()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);
        var estimate = Estimate(promptTokens: 10_000, maxOutputTokens: 5_000, costGbp: 5.00m);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveAsync_WithinAllBudgets_ReturnsTrue()
    {
        var policy = PolicyWith(
            perInvocation: new BudgetSpec(MaxTokens: 20_000, MaxCostGbp: 5.00m, MaxAgentInvocations: 10),
            perExecution: new BudgetSpec(MaxTokens: 50_000, MaxCostGbp: 20.00m, MaxAgentInvocations: null),
            perEngagement: new BudgetSpec(MaxTokens: 100_000, MaxCostGbp: 50.00m, MaxAgentInvocations: null));
        var hierarchy = Hierarchy(policy);
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500, costGbp: 0.10m);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveAsync_InvocationTokenBudgetBreached_ReturnsFalse()
    {
        // PerInvocation budget: 1000 tokens. Estimate: 800 prompt + 201 output = 1001 > 1000.
        var hierarchy = Hierarchy(PolicyWith(perInvocation: new BudgetSpec(MaxTokens: 1_000, null, null)));
        var estimate = Estimate(promptTokens: 800, maxOutputTokens: 201, costGbp: 0.01m);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveAsync_InvocationCostBudgetBreached_ReturnsFalse()
    {
        var hierarchy = Hierarchy(PolicyWith(perInvocation: new BudgetSpec(null, MaxCostGbp: 1.00m, null)));
        var estimate = Estimate(promptTokens: 100, maxOutputTokens: 100, costGbp: 1.01m);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveAsync_ExecutionAccumulatedTokensBudgetBreached_ReturnsFalse()
    {
        // PerExecution budget: 1000 tokens. Seed 500+500 already used in execution. Estimate adds 1+1 → 1002 > 1000.
        var executionId = "exec-over-limit";
        var hierarchy = Hierarchy(PolicyWith(perExecution: new BudgetSpec(MaxTokens: 1_000, null, null)));
        await ledger.RecordUsageAsync(Usage("corr-a", executionId, "eng-1", inputTokens: 300, outputTokens: 200, costGbp: 0.01m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("corr-b", executionId, "eng-1", inputTokens: 300, outputTokens: 200, costGbp: 0.01m), CancellationToken.None);
        var estimate = Estimate(promptTokens: 1, maxOutputTokens: 1, costGbp: 0.001m, executionId: executionId);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveAsync_ExecutionMaxInvocationsBreached_ReturnsFalse()
    {
        // PerExecution budget: 3 invocations. Seed 3 prior records → count 3. New: 3+1=4 > 3 → deny.
        var executionId = "exec-at-limit";
        var hierarchy = Hierarchy(PolicyWith(perExecution: new BudgetSpec(null, null, MaxAgentInvocations: 3)));
        await ledger.RecordUsageAsync(Usage("corr-1", executionId, "eng-1", 100, 50, 0.01m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("corr-2", executionId, "eng-1", 100, 50, 0.01m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("corr-3", executionId, "eng-1", 100, 50, 0.01m), CancellationToken.None);
        var estimate = Estimate(promptTokens: 100, maxOutputTokens: 50, costGbp: 0.01m, executionId: executionId);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveAsync_EngagementCostBudgetBreached_ReturnsFalse()
    {
        // PerEngagement cost budget: £5. Seed £4.90 across two executions. Estimate £0.11 → 5.01 > 5 → deny.
        var engagementId = "eng-over-cost";
        var hierarchy = Hierarchy(PolicyWith(perEngagement: new BudgetSpec(null, MaxCostGbp: 5.00m, null)));
        await ledger.RecordUsageAsync(Usage("corr-a", "exec-1", engagementId, 100, 50, costGbp: 2.45m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("corr-b", "exec-2", engagementId, 100, 50, costGbp: 2.45m), CancellationToken.None);
        var estimate = Estimate(promptTokens: 100, maxOutputTokens: 50, costGbp: 0.11m, engagementId: engagementId);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, engagementId),
            estimate,
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task CanApproveAsync_NullPerExecutionBudget_SkipsExecutionCheck()
    {
        // PerInvocation and PerEngagement set, PerExecution null. Execution level is skipped; should approve.
        var hierarchy = Hierarchy(PolicyWith(
            perInvocation: new BudgetSpec(MaxTokens: 10_000, null, null),
            perEngagement: new BudgetSpec(MaxTokens: 100_000, null, null)));
        var estimate = Estimate(promptTokens: 1_000, maxOutputTokens: 500, costGbp: 0.01m);

        var result = await hierarchy.CanApproveAsync(
            Scope(BudgetScopeKind.Invocation, estimate.CorrelationId),
            Scope(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None);

        Assert.True(result);
    }

    // ── CanApproveAtScopeAsync tests (internal method) ────────────────────────

    [Fact]
    public async Task CanApproveAtScopeAsync_NullBudget_ReturnsTrue()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);
        var scope = Scope(BudgetScopeKind.Invocation, "corr-1");

        var result = await hierarchy.CanApproveAtScopeAsync(scope, budget: null, Estimate(), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task CanApproveAtScopeAsync_BudgetWithCapacity_ReturnsTrue()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);
        var scope = Scope(BudgetScopeKind.Invocation, "corr-new");
        var budget = new BudgetSpec(MaxTokens: 10_000, MaxCostGbp: 5.00m, MaxAgentInvocations: null);

        var result = await hierarchy.CanApproveAtScopeAsync(scope, budget, Estimate(promptTokens: 100, maxOutputTokens: 100), CancellationToken.None);

        Assert.True(result);
    }

    // ── BudgetHasCapacity tests (internal static method) ─────────────────────

    [Fact]
    public void BudgetHasCapacity_AllBudgetFieldsNull_ReturnsTrue()
    {
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Invocation, "x"), TokensUsed: 9_999, CostGbp: 4.99m, InvocationCount: 9);
        var budget = new BudgetSpec(MaxTokens: null, MaxCostGbp: null, MaxAgentInvocations: null);

        Assert.True(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate(promptTokens: 1_000, maxOutputTokens: 1_000)));
    }

    [Fact]
    public void BudgetHasCapacity_ExactlyAtTokenLimit_ReturnsTrue()
    {
        // snapshot.TokensUsed + prompt + output == MaxTokens → still approved (not strictly over)
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Invocation, "x"), TokensUsed: 18_000, CostGbp: 0m, InvocationCount: 0);
        var budget = new BudgetSpec(MaxTokens: 20_000, null, null);

        Assert.True(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate(promptTokens: 1_000, maxOutputTokens: 1_000)));
    }

    [Fact]
    public void BudgetHasCapacity_OneTokenOverLimit_ReturnsFalse()
    {
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Invocation, "x"), TokensUsed: 18_001, CostGbp: 0m, InvocationCount: 0);
        var budget = new BudgetSpec(MaxTokens: 20_000, null, null);

        Assert.False(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate(promptTokens: 1_000, maxOutputTokens: 1_000)));
    }

    [Fact]
    public void BudgetHasCapacity_CostExceeded_ReturnsFalse()
    {
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Engagement, "eng-1"), TokensUsed: 0, CostGbp: 4.99m, InvocationCount: 0);
        var budget = new BudgetSpec(null, MaxCostGbp: 5.00m, null);

        Assert.False(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate(costGbp: 0.02m)));
    }

    [Fact]
    public void BudgetHasCapacity_MaxInvocationsExceeded_ReturnsFalse()
    {
        // snapshot.InvocationCount + 1 > MaxAgentInvocations
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Execution, "exec-1"), TokensUsed: 0, CostGbp: 0m, InvocationCount: 5);
        var budget = new BudgetSpec(null, null, MaxAgentInvocations: 5);

        Assert.False(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate()));
    }

    [Fact]
    public void BudgetHasCapacity_ExactlyAtInvocationLimit_ReturnsTrue()
    {
        // snapshot.InvocationCount + 1 == MaxAgentInvocations → still approved
        var snapshot = new BudgetSnapshot(Scope(BudgetScopeKind.Execution, "exec-1"), TokensUsed: 0, CostGbp: 0m, InvocationCount: 4);
        var budget = new BudgetSpec(null, null, MaxAgentInvocations: 5);

        Assert.True(BudgetHierarchy.BudgetHasCapacity(snapshot, budget, Estimate()));
    }

    // ── RecordHierarchicalUsageAsync tests ────────────────────────────────────

    [Fact]
    public async Task RecordHierarchicalUsageAsync_NullInvocationScope_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.RecordHierarchicalUsageAsync(null!, Scope(BudgetScopeKind.Engagement, "eng-1"), Usage("c", "e", "g", 1, 1, 0.01m), CancellationToken.None));
    }

    [Fact]
    public async Task RecordHierarchicalUsageAsync_NullEngagementScope_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.RecordHierarchicalUsageAsync(Scope(BudgetScopeKind.Invocation, "corr-1"), null!, Usage("c", "e", "g", 1, 1, 0.01m), CancellationToken.None));
    }

    [Fact]
    public async Task RecordHierarchicalUsageAsync_NullUsage_Throws()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            hierarchy.RecordHierarchicalUsageAsync(Scope(BudgetScopeKind.Invocation, "corr-1"), Scope(BudgetScopeKind.Engagement, "eng-1"), null!, CancellationToken.None));
    }

    [Fact]
    public async Task RecordHierarchicalUsageAsync_SingleRecordVisibleAtAllScopes()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);
        var usage = Usage("corr-1", "exec-1", "eng-1", inputTokens: 100, outputTokens: 50, costGbp: 0.10m);

        await hierarchy.RecordHierarchicalUsageAsync(
            Scope(BudgetScopeKind.Invocation, "corr-1"),
            Scope(BudgetScopeKind.Engagement, "eng-1"),
            usage,
            CancellationToken.None);

        // Ledger aggregates by scope kind — a single stored record matches all three scope levels.
        var invocationSnapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Invocation, "corr-1"), CancellationToken.None);
        var executionSnapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1"), CancellationToken.None);
        var engagementSnapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1"), CancellationToken.None);

        Assert.Equal(150, invocationSnapshot.TokensUsed);
        Assert.Equal(1, invocationSnapshot.InvocationCount);
        Assert.Equal(150, executionSnapshot.TokensUsed);
        Assert.Equal(1, executionSnapshot.InvocationCount);
        Assert.Equal(150, engagementSnapshot.TokensUsed);
        Assert.Equal(1, engagementSnapshot.InvocationCount);
    }

    [Fact]
    public async Task RecordHierarchicalUsageAsync_CalledTwiceWithSameCorrelationId_DoesNotDoubleCount()
    {
        var hierarchy = Hierarchy(UnboundedPolicy);
        var usage = Usage("corr-idem", "exec-1", "eng-1", inputTokens: 100, outputTokens: 50, costGbp: 0.10m);

        await hierarchy.RecordHierarchicalUsageAsync(Scope(BudgetScopeKind.Invocation, "corr-idem"), Scope(BudgetScopeKind.Engagement, "eng-1"), usage, CancellationToken.None);
        await hierarchy.RecordHierarchicalUsageAsync(Scope(BudgetScopeKind.Invocation, "corr-idem"), Scope(BudgetScopeKind.Engagement, "eng-1"), usage with { InputTokens = 9999 }, CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1"), CancellationToken.None);
        Assert.Equal(150, snapshot.TokensUsed);
        Assert.Equal(1, snapshot.InvocationCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private BudgetHierarchy Hierarchy(GuardrailPolicy policy) => new(ledger, policy);

    private static readonly GuardrailPolicy UnboundedPolicy = new("unbounded", PerInvocation: null, PerExecution: null, PerEngagement: null);

    private static GuardrailPolicy PolicyWith(
        BudgetSpec? perInvocation = null,
        BudgetSpec? perExecution = null,
        BudgetSpec? perEngagement = null) =>
        new("test-policy", perInvocation, perExecution, perEngagement);

    private static BudgetScopeRef Scope(BudgetScopeKind kind, string id) => new(kind, id);

    private static InvocationCostEstimate Estimate(
        long promptTokens = 100,
        long maxOutputTokens = 100,
        decimal costGbp = 0.01m,
        string? executionId = null,
        string? engagementId = null) => new(
            CorrelationId: "corr-test",
            ExecutionId: executionId ?? "exec-test",
            EngagementId: engagementId ?? "eng-test",
            NodeId: "gen-scope",
            AgentRole: "deep-reasoning",
            ResolvedModel: "claude-fable-5",
            PromptTokens: promptTokens,
            MaxOutputTokens: maxOutputTokens,
            EstimatedCostGbp: costGbp);

    private static UsageRecord Usage(string correlationId, string executionId, string engagementId, long inputTokens, long outputTokens, decimal costGbp) => new(
        CorrelationId: correlationId,
        ExecutionId: executionId,
        EngagementId: engagementId,
        NodeId: "gen-scope",
        AgentRole: "deep-reasoning",
        ResolvedModel: "claude-fable-5",
        InputTokens: inputTokens,
        OutputTokens: outputTokens,
        CostGbp: costGbp);
}
