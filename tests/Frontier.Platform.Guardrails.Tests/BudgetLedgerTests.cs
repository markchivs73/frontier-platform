namespace Frontier.Platform.Guardrails.Tests;

/// <summary>S4.5 tests for <see cref="BudgetLedger"/> (doc 07 §6 in-memory PoC ledger).</summary>
public sealed class BudgetLedgerTests
{
    private readonly BudgetLedger ledger = new();

    [Fact]
    public async Task RecordUsageAsync_NullUsage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.RecordUsageAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_NullScope_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.GetSnapshotAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetSnapshotAsync_NoMatchingRecords_ReturnsZeroSnapshot()
    {
        var scope = new BudgetScopeRef(BudgetScopeKind.Invocation, "unknown-correlation");

        var snapshot = await ledger.GetSnapshotAsync(scope, CancellationToken.None);

        Assert.Equal(0, snapshot.TokensUsed);
        Assert.Equal(0m, snapshot.CostGbp);
        Assert.Equal(0, snapshot.InvocationCount);
    }

    [Fact]
    public async Task RecordUsageAsync_DuplicateCorrelationId_DoesNotDoubleCount()
    {
        var usage = Usage(correlationId: "correlation-1", executionId: "execution-1", engagementId: "engagement-1", inputTokens: 100, outputTokens: 50, costGbp: 0.10m);

        await ledger.RecordUsageAsync(usage, CancellationToken.None);
        await ledger.RecordUsageAsync(usage with { InputTokens = 999 }, CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Invocation, "correlation-1"), CancellationToken.None);

        Assert.Equal(150, snapshot.TokensUsed);
        Assert.Equal(0.10m, snapshot.CostGbp);
        Assert.Equal(1, snapshot.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_InvocationScope_MatchesByCorrelationId()
    {
        await ledger.RecordUsageAsync(Usage("correlation-a", "execution-1", "engagement-1", 100, 50, 0.10m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("correlation-b", "execution-1", "engagement-1", 200, 75, 0.20m), CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Invocation, "correlation-a"), CancellationToken.None);

        Assert.Equal(150, snapshot.TokensUsed);
        Assert.Equal(0.10m, snapshot.CostGbp);
        Assert.Equal(1, snapshot.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ExecutionScope_AggregatesAllNodesInExecution()
    {
        await ledger.RecordUsageAsync(Usage("correlation-a", "execution-1", "engagement-1", 100, 50, 0.10m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("correlation-b", "execution-1", "engagement-1", 200, 75, 0.20m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("correlation-c", "execution-2", "engagement-1", 1_000, 1_000, 1.00m), CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Execution, "execution-1"), CancellationToken.None);

        Assert.Equal(425, snapshot.TokensUsed);
        Assert.Equal(0.30m, snapshot.CostGbp);
        Assert.Equal(2, snapshot.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_EngagementScope_AggregatesAllExecutionsInEngagement()
    {
        await ledger.RecordUsageAsync(Usage("correlation-a", "execution-1", "engagement-1", 100, 50, 0.10m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("correlation-b", "execution-2", "engagement-1", 200, 75, 0.20m), CancellationToken.None);
        await ledger.RecordUsageAsync(Usage("correlation-c", "execution-3", "engagement-2", 1_000, 1_000, 1.00m), CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Engagement, "engagement-1"), CancellationToken.None);

        Assert.Equal(425, snapshot.TokensUsed);
        Assert.Equal(0.30m, snapshot.CostGbp);
        Assert.Equal(2, snapshot.InvocationCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_FleetScope_NeverMatches()
    {
        await ledger.RecordUsageAsync(Usage("correlation-a", "execution-1", "engagement-1", 100, 50, 0.10m), CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Fleet, "ignored"), CancellationToken.None);

        Assert.Equal(0, snapshot.TokensUsed);
        Assert.Equal(0m, snapshot.CostGbp);
        Assert.Equal(0, snapshot.InvocationCount);
    }

    internal static UsageRecord Usage(string correlationId, string executionId, string engagementId, long inputTokens, long outputTokens, decimal costGbp) => new(
        CorrelationId: correlationId,
        ExecutionId: executionId,
        EngagementId: engagementId,
        NodeId: "gen-pricing",
        AgentRole: "deep-reasoning",
        ResolvedModel: "claude-fable-5",
        InputTokens: inputTokens,
        OutputTokens: outputTokens,
        CostGbp: costGbp);
}
