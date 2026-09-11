using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Guardrails.Tests;

/// <summary>ADR-PA21: amounts carry an ISO 4217 currency, and combining two currencies is a permanent contract violation.</summary>
public sealed class CostCurrencyTests
{
    // ── CostCurrency ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "USD")]
    [InlineData("USD", "USD")]
    public void EnsureCombinable_NothingAccumulatedOrSameCurrency_DoesNotThrow(string? combined, string incoming)
    {
        CostCurrency.EnsureCombinable("Test", combined, incoming);
    }

    [Theory]
    [InlineData("GBP", "USD")]
    [InlineData("USD", null)]
    [InlineData("USD", "usd")]
    public void EnsureCombinable_DifferentCurrency_ThrowsContractViolation(string combined, string? incoming)
    {
        var ex = Assert.Throws<ContractViolationException>(() => CostCurrency.EnsureCombinable("Test", combined, incoming));

        Assert.Equal("Test", ex.ContractType);
        Assert.Contains("ADR-PA21", Assert.Single(ex.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureComparable_NoCostCeiling_IgnoresCurrency()
    {
        CostCurrency.EnsureComparable(new BudgetSpec(1_000, null, null, null), Estimate("GBP"));
    }

    [Fact]
    public void EnsureComparable_CeilingInEstimateCurrency_DoesNotThrow()
    {
        CostCurrency.EnsureComparable(new BudgetSpec(null, 1.00m, "USD", null), Estimate("USD"));
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData(null)]
    public void EnsureComparable_CeilingInOtherOrNoCurrency_ThrowsContractViolation(string? budgetCurrency)
    {
        var ex = Assert.Throws<ContractViolationException>(() =>
            CostCurrency.EnsureComparable(new BudgetSpec(null, 1.00m, budgetCurrency, null), Estimate("USD")));

        Assert.Equal(nameof(BudgetSpec), ex.ContractType);
    }

    // ── Admission: estimate vs. ceiling ───────────────────────────────────────

    [Fact]
    public void Admit_CostCeilingInOtherCurrency_ThrowsContractViolation()
    {
        var policy = new GuardrailPolicy("gbp-policy", new BudgetSpec(20_000, 2.00m, "GBP", null), null, null);

        Assert.Throws<ContractViolationException>(() => AdmissionController.Admit(Estimate("USD"), policy));
    }

    [Fact]
    public void Admit_Phase1Policies_AdmitAUsdEstimate()
    {
        Assert.Equal(AdmissionResult.Proceed, AdmissionController.Admit(Estimate("USD"), Phase1GuardrailPolicyCatalogue.Default).Result);
        Assert.Equal(AdmissionResult.Proceed, AdmissionController.Admit(Estimate("USD"), Phase1GuardrailPolicyCatalogue.Sandbox).Result);
    }

    [Fact]
    public void BudgetHasCapacity_CeilingInOtherCurrency_ThrowsContractViolation()
    {
        var snapshot = new BudgetSnapshot(new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1"), 0, 0m, null, 0);

        Assert.Throws<ContractViolationException>(() =>
            BudgetHierarchy.BudgetHasCapacity(snapshot, new BudgetSpec(null, 5.00m, "GBP", null), Estimate("USD")));
    }

    [Fact]
    public void BudgetHasCapacity_AccumulatedCostInOtherCurrency_ThrowsContractViolation()
    {
        var snapshot = new BudgetSnapshot(new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1"), 0, 1.00m, "GBP", 1);

        var ex = Assert.Throws<ContractViolationException>(() =>
            BudgetHierarchy.BudgetHasCapacity(snapshot, new BudgetSpec(null, 5.00m, "USD", null), Estimate("USD")));

        Assert.Equal(nameof(BudgetSnapshot), ex.ContractType);
    }

    [Fact]
    public void BudgetHasCapacity_EmptySnapshot_ComparesEstimateWithCeiling()
    {
        var snapshot = new BudgetSnapshot(new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1"), 0, 0m, null, 0);

        Assert.True(BudgetHierarchy.BudgetHasCapacity(snapshot, new BudgetSpec(null, 5.00m, "USD", null), Estimate("USD")));
    }

    // ── Ledger accumulation and hierarchy aggregation ─────────────────────────

    [Fact]
    public async Task RecordUsageAsync_SecondCurrencyInOneEngagement_ThrowsAndIsNotStored()
    {
        var ledger = new BudgetLedger();
        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-a", "exec-1", "eng-1", 100, 50, 0.10m, "USD"), CancellationToken.None);

        await Assert.ThrowsAsync<ContractViolationException>(() =>
            ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-b", "exec-2", "eng-1", 100, 50, 0.10m, "GBP"), CancellationToken.None));

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-1"), CancellationToken.None);
        Assert.Equal(1, snapshot.InvocationCount);
        Assert.Equal("USD", snapshot.Currency);
    }

    [Fact]
    public async Task RecordUsageAsync_DifferentEngagements_MayUseDifferentCurrencies()
    {
        var ledger = new BudgetLedger();
        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-a", "exec-1", "eng-1", 100, 50, 0.10m, "USD"), CancellationToken.None);
        await ledger.RecordUsageAsync(BudgetLedgerTests.Usage("corr-b", "exec-2", "eng-2", 100, 50, 0.10m, "GBP"), CancellationToken.None);

        var snapshot = await ledger.GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Engagement, "eng-2"), CancellationToken.None);
        Assert.Equal("GBP", snapshot.Currency);
    }

    [Fact]
    public async Task GetSnapshotAsync_NothingRecorded_HasNoCurrency()
    {
        var snapshot = await new BudgetLedger().GetSnapshotAsync(new BudgetScopeRef(BudgetScopeKind.Execution, "exec-1"), CancellationToken.None);

        Assert.Null(snapshot.Currency);
    }

    [Fact]
    public async Task CanApproveAsync_EngagementCeilingInOtherCurrency_ThrowsContractViolation()
    {
        var ledger = new BudgetLedger();
        var policy = new GuardrailPolicy("mixed", null, new BudgetSpec(null, 20.00m, "USD", null), new BudgetSpec(null, 50.00m, "GBP", null));
        var hierarchy = new BudgetHierarchy(ledger, policy);
        var estimate = Estimate("USD");

        await Assert.ThrowsAsync<ContractViolationException>(() => hierarchy.CanApproveAsync(
            new BudgetScopeRef(BudgetScopeKind.Invocation, estimate.CorrelationId),
            new BudgetScopeRef(BudgetScopeKind.Engagement, estimate.EngagementId),
            estimate,
            CancellationToken.None));
    }

    // ── Stored document ───────────────────────────────────────────────────────

    [Fact]
    public void BudgetLedgerDocument_RoundTripsThroughCanonicalProfile_WithCurrency()
    {
        var document = new BudgetLedgerDocument
        {
            PartitionKey = "eng-1",
            Id = "eng-1:ledger",
            EngagementId = "eng-1",
            TotalInputTokens = 1_000,
            TotalOutputTokens = 500,
            TotalCost = 1.32m,
            Currency = "USD",
            InvocationCount = 7,
            ExecutionSnapshots = new Dictionary<string, ExecutionLedgerSnapshot>
            {
                ["exec-1"] = new("exec-1", 1_500, 1.32m, "USD", 7, new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)),
            },
        };

        var bytes = CanonicalProfile.SerializeCanonical(document);
        var json = JsonNode.Parse(bytes)!.AsObject();
        var roundTripped = JsonSerializer.Deserialize<BudgetLedgerDocument>(bytes, CanonicalProfile.Options)!;

        Assert.Equal(["partitionKey", "id", "engagement_id", "total_input_tokens", "total_output_tokens", "total_cost", "currency", "invocation_count", "execution_snapshots"], json.Select(p => p.Key));
        Assert.Equal("USD", (string?)json["currency"]);
        Assert.Equal("USD", (string?)json["execution_snapshots"]!["exec-1"]!["currency"]);
        Assert.Equal(1.32m, roundTripped.TotalCost);
        Assert.Equal("USD", roundTripped.Currency);
        Assert.Equal(document.ExecutionSnapshots["exec-1"], roundTripped.ExecutionSnapshots!["exec-1"]);
    }

    private static InvocationCostEstimate Estimate(string currency) => new(
        CorrelationId: "corr-1",
        ExecutionId: "exec-1",
        EngagementId: "eng-1",
        NodeId: "gen-scope",
        AgentRole: "deep-reasoning",
        ResolvedModel: "claude-opus-4-8",
        PromptTokens: 100,
        MaxOutputTokens: 100,
        EstimatedCost: 0.01m,
        Currency: currency);
}
