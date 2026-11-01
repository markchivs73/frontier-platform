using Frontier.TestSupport;

namespace Frontier.Platform.Abstractions.Tests.Serialization;

/// <summary>ADR-PA43: the validator budgets record's wire shape, its defaults and its range checks.</summary>
public sealed class ContractBudgetsTests
{
    [Fact]
    public void Budgets_AreByteStable_MatchTheirGoldenFile_AndRoundTrip() =>
        ContractRoundTripAssertions.AssertStableAndRoundTrips(Sample(), "contract_budgets.json");

    [Fact]
    public void Default_IsTodaysConfiguredDefaults()
    {
        var defaults = ContractBudgets.Default;

        Assert.Equal((1_000_000, 100_000, 64, 20), (defaults.MaxRuleCost, defaults.MaxRuleIterations, defaults.MaxPayloadDepth, defaults.MaxViolations));
        Assert.Equal("1.0", defaults.SchemaVersion);
    }

    [Fact]
    public void Validate_TheDefaultsAndTheCeilings_DoNotThrow()
    {
        ContractBudgets.Default.Validate();
        (ContractBudgets.Default with { MaxRuleCost = int.MaxValue, MaxRuleIterations = int.MaxValue, MaxPayloadDepth = ContractBudgets.PayloadDepthCeiling, MaxViolations = ContractBudgets.ViolationsCeiling }).Validate();
    }

    [Fact]
    public void Validate_ReportsEveryBudgetBelowOne()
    {
        var budgets = new ContractBudgets { MaxRuleCost = 0, MaxRuleIterations = -1, MaxPayloadDepth = 0, MaxViolations = 0 };

        var exception = Assert.Throws<ContractViolationException>(budgets.Validate);

        Assert.Equal(nameof(ContractBudgets), exception.ContractType);
        Assert.Equal(
            ["max_rule_cost must be between 1 and 2147483647", "max_rule_iterations must be between 1 and 2147483647", "max_payload_depth must be between 1 and 256", "max_violations must be between 1 and 1000"],
            exception.Violations);
    }

    [Fact]
    public void Validate_ReportsADepthOrViolationCountOverItsCeiling()
    {
        var budgets = ContractBudgets.Default with { MaxPayloadDepth = ContractBudgets.PayloadDepthCeiling + 1, MaxViolations = ContractBudgets.ViolationsCeiling + 1 };

        var exception = Assert.Throws<ContractViolationException>(budgets.Validate);

        Assert.Equal(["max_payload_depth must be between 1 and 256", "max_violations must be between 1 and 1000"], exception.Violations);
    }

    private static ContractBudgets Sample() => new() { MaxRuleCost = 500_000, MaxRuleIterations = 50_000, MaxPayloadDepth = 32, MaxViolations = 10 };
}
