using System.Text;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>
/// The ledger document's accumulation (doc 07 §6): engagement totals and the per-execution snapshot both advance on
/// every recorded usage, one currency per snapshot (ADR-PA21), and costs are written at scale 4.
/// </summary>
public sealed class BudgetLedgerAccumulationTests
{
    private static readonly DateTime First = new(2026, 9, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Second = new(2026, 9, 11, 9, 5, 0, DateTimeKind.Utc);

    // ── AccumulateExecution ───────────────────────────────────────────────────

    [Fact]
    public void AccumulateExecution_NewExecution_StartsAFreshSnapshot()
    {
        var snapshot = BudgetLedgerAccumulation.AccumulateExecution(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        Assert.Equal(new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 1, First), snapshot);
    }

    [Fact]
    public void AccumulateExecution_SameExecution_AccumulatesTokensCostAndCount()
    {
        var existing = new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 1, First);

        var snapshot = BudgetLedgerAccumulation.AccumulateExecution(existing, BudgetLedgerTests.Usage("corr-2", "exec-1", "eng-1", 200, 25, 0.0030m), Second);

        Assert.Equal(new ExecutionLedgerSnapshot("exec-1", 375, 0.0042m, "USD", 2, Second), snapshot);
    }

    [Fact]
    public void AccumulateExecution_CurrencyDiffersFromSnapshot_ThrowsContractViolation()
    {
        var existing = new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 1, First);

        var ex = Assert.Throws<ContractViolationException>(() =>
            BudgetLedgerAccumulation.AccumulateExecution(existing, BudgetLedgerTests.Usage("corr-2", "exec-1", "eng-1", 1, 1, 0.01m, "GBP"), Second));

        Assert.Equal(nameof(ExecutionLedgerSnapshot), ex.ContractType);
    }

    // ── Accumulate ────────────────────────────────────────────────────────────

    [Fact]
    public void Accumulate_NoDocument_CreatesOneWithTheExecutionSnapshot()
    {
        var document = BudgetLedgerAccumulation.Accumulate(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        Assert.Equal(("eng-1", "eng-1:ledger", "eng-1", "USD"), (document.PartitionKey, document.Id, document.EngagementId, document.Currency));
        Assert.Equal((100L, 50L, 0.0012m, 1), (document.TotalInputTokens, document.TotalOutputTokens, document.TotalCost, document.InvocationCount));
        Assert.Equal(new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 1, First), Assert.Single(document.ExecutionSnapshots!).Value);
    }

    [Fact]
    public void Accumulate_SameExecution_UpdatesItsSnapshot()
    {
        var created = BudgetLedgerAccumulation.Accumulate(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        var document = BudgetLedgerAccumulation.Accumulate(created, BudgetLedgerTests.Usage("corr-2", "exec-1", "eng-1", 200, 25, 0.0030m), Second);

        Assert.Equal((300L, 75L, 0.0042m, 2), (document.TotalInputTokens, document.TotalOutputTokens, document.TotalCost, document.InvocationCount));
        Assert.Equal(new ExecutionLedgerSnapshot("exec-1", 375, 0.0042m, "USD", 2, Second), Assert.Single(document.ExecutionSnapshots!).Value);
    }

    [Fact]
    public void Accumulate_DifferentExecution_GetsItsOwnSnapshotAndLeavesTheOriginalUntouched()
    {
        var created = BudgetLedgerAccumulation.Accumulate(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        var document = BudgetLedgerAccumulation.Accumulate(created, BudgetLedgerTests.Usage("corr-2", "exec-2", "eng-1", 10, 5, 0.0003m), Second);

        Assert.Equal(new ExecutionLedgerSnapshot("exec-1", 150, 0.0012m, "USD", 1, First), document.ExecutionSnapshots!["exec-1"]);
        Assert.Equal(new ExecutionLedgerSnapshot("exec-2", 15, 0.0003m, "USD", 1, Second), document.ExecutionSnapshots["exec-2"]);
        Assert.Single(created.ExecutionSnapshots!);
    }

    [Fact]
    public void Accumulate_DocumentWithoutSnapshots_AddsTheExecution()
    {
        var legacy = BudgetLedgerAccumulation.Empty(BudgetLedgerTests.Usage("corr-0", "exec-0", "eng-1", 0, 0, 0m)) with { InvocationCount = 3 };

        var document = BudgetLedgerAccumulation.Accumulate(legacy, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 1, 1, 0.0001m), First);

        Assert.Equal(4, document.InvocationCount);
        Assert.Equal("exec-1", Assert.Single(document.ExecutionSnapshots!).Key);
    }

    [Fact]
    public void Accumulate_CurrencyDiffersFromDocument_ThrowsContractViolation()
    {
        var created = BudgetLedgerAccumulation.Accumulate(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        var ex = Assert.Throws<ContractViolationException>(() =>
            BudgetLedgerAccumulation.Accumulate(created, BudgetLedgerTests.Usage("corr-2", "exec-2", "eng-1", 1, 1, 0.01m, "GBP"), Second));

        Assert.Equal(nameof(BudgetLedgerDocument), ex.ContractType);
    }

    // ── Wire scale ────────────────────────────────────────────────────────────

    [Fact]
    public void LedgerCostFields_AreWrittenAtScale4()
    {
        var document = BudgetLedgerAccumulation.Accumulate(null, BudgetLedgerTests.Usage("corr-1", "exec-1", "eng-1", 100, 50, 0.0012m), First);

        var json = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(document));

        Assert.Contains("\"total_cost\":\"0.0012\",\"currency\":\"USD\",\"invocation_count\":1,\"execution_snapshots\"", json, StringComparison.Ordinal);
        Assert.Contains("\"exec-1\":{\"execution_id\":\"exec-1\",\"total_tokens\":150,\"total_cost\":\"0.0012\"", json, StringComparison.Ordinal);
    }
}
