using System.Text;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>
/// ADR-PA43: the validator and the registration check take a run's pinned budgets per call. One
/// compilation serves every budget, the cost ceiling is a registration check only, and a call without
/// budgets uses <see cref="ContractsOptions"/> as before.
/// </summary>
public sealed class ContractBudgetsTests
{
    /// <summary>A legible contract whose one rule iterates a bounded array.</summary>
    private const string CodesSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "description": "A list of codes. Not for raw evidence.",
          "type": "object",
          "properties": {
            "codes": { "description": "The codes.", "type": "array", "maxItems": 50, "items": { "type": "string", "maxLength": 5 } }
          },
          "required": ["codes"],
          "additionalProperties": false,
          "examples": [ { "codes": ["a", "b", "c", "d", "e"] } ]
        }
        """;

    private const string FiveCodes = """{ "codes": ["a", "b", "c", "d", "e"] }""";

    private static readonly ContractRule[] NoX = [new() { Rule = "self.codes.all(c, c != 'x')", Message = "No x." }];

    private static readonly ContractSnapshot Codes = ContractValidatorTests.Snapshot(CodesSchema, NoX);

    private static readonly ContractSnapshot Legible = ContractValidatorTests.Snapshot(Json.LegibleSchema);

    [Fact]
    public void ConfiguredBudgets_AreTheFourOptions()
    {
        var options = new ContractsOptions { MaxRuleCost = 1, MaxRuleIterations = 2, MaxPayloadDepth = 3, MaxViolations = 4 };

        Assert.Equal(new ContractBudgets { MaxRuleCost = 1, MaxRuleIterations = 2, MaxPayloadDepth = 3, MaxViolations = 4 }, ConfiguredBudgets.From(options));
        Assert.Equal(ContractBudgets.Default, ConfiguredBudgets.From(new ContractsOptions()));
    }

    [Fact]
    public void Validate_WithBudgets_StopsAtTheirMaxViolations()
    {
        var payload = Json.Parse("""{ "total": "x", "label": "far too long a label", "other": 1 }""");

        Assert.Single(Validator().Validate(Legible, payload, ContractBudgets.Default with { MaxViolations = 1 }).Violations);
        Assert.Equal(3, Validator().Validate(Legible, payload).Violations.Count);
    }

    [Fact]
    public void Validate_WithBudgets_HoldsBytesAndElementsToTheirDepth()
    {
        var budgets = ContractBudgets.Default with { MaxPayloadDepth = 3 };
        var snapshot = ContractValidatorTests.Snapshot("{}");

        var fromBytes = Assert.Single(Validator().Validate(snapshot, Encoding.UTF8.GetBytes("[[[[1]]]]"), budgets).Violations);
        Assert.Contains("3 levels", fromBytes.Message, StringComparison.Ordinal);
        Assert.False(Validator().Validate(snapshot, Json.Parse("[[[[1]]]]"), budgets).IsValid);
        Assert.True(Validator().Validate(snapshot, Json.Parse("[[[[1]]]]")).IsValid);
    }

    [Fact]
    public void Validate_WithBudgets_GivesEachRuleTheirIterations_FromOneCompilation()
    {
        var engine = new CountingEngine(Engine.Real());
        var validator = new ContractValidator(engine, Engine.Rules(), Engine.Options());

        var starved = Assert.Single(validator.Validate(Codes, Json.Parse(FiveCodes), ContractBudgets.Default with { MaxRuleIterations = 2 }).Violations);

        Assert.Equal("No x. (the rule ran past its evaluation budget)", starved.Message);
        Assert.True(validator.Validate(Codes, Json.Parse(FiveCodes)).IsValid);
        Assert.True(validator.Validate(Codes, Encoding.UTF8.GetBytes(FiveCodes), ContractBudgets.Default).IsValid);
        Assert.Equal(1, engine.Compilations);
    }

    [Fact]
    public void Validate_DoesNotCheckTheCostCeiling_WhichRegistrationAlreadyDid()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options(o => o.MaxRuleCost = 1));

        Assert.True(validator.Validate(Codes, Json.Parse(FiveCodes)).IsValid);
        Assert.True(validator.Validate(Codes, Json.Parse(FiveCodes), ContractBudgets.Default with { MaxRuleCost = 1 }).IsValid);
    }

    [Fact]
    public void Validate_StillRefusesARuleWhoseCostIsUnbounded()
    {
        var contract = ContractValidatorTests.Snapshot("""{ "type": "object", "properties": { "note": { "type": "string" } } }""", new ContractRule { Rule = "self.note == 'a'", Message = "m" });

        var exception = Assert.Throws<ContractViolationException>(() => Validator().Validate(contract, Json.Parse("{}"), ContractBudgets.Default));
        Assert.Contains("a rule does not compile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RefusesMissingOrOutOfRangeBudgets()
    {
        var invalid = ContractBudgets.Default with { MaxViolations = 0 };

        Assert.Throws<ArgumentNullException>(() => Validator().Validate(Legible, Json.Parse("{}"), null!));
        Assert.Throws<ArgumentNullException>(() => Validator().Validate(Legible, ReadOnlyMemory<byte>.Empty, null!));
        Assert.Equal(nameof(ContractBudgets), Assert.Throws<ContractViolationException>(() => Validator().Validate(Legible, Json.Parse("{}"), invalid)).ContractType);
        Assert.Throws<ContractViolationException>(() => Validator().Validate(Legible, ReadOnlyMemory<byte>.Empty, invalid));
        Assert.Throws<ArgumentNullException>(() => Validator().Validate(null!, Json.Parse("{}"), ContractBudgets.Default));
        Assert.Throws<ArgumentNullException>(() => Validator().Validate(null!, ReadOnlyMemory<byte>.Empty, ContractBudgets.Default));
    }

    [Fact]
    public void Check_WithBudgets_HoldsEveryRuleToTheirCostCeiling()
    {
        var refused = Check().Check(Request(), [], ContractBudgets.Default with { MaxRuleCost = 10 });
        var accepted = Check().Check(Request(), [], ContractBudgets.Default);

        Assert.Contains(refused.Findings, f => f.Code == ContractFindingCodes.RuleCostExceeded && f.Message.Contains("over the limit of 10", StringComparison.Ordinal));
        Assert.Null(refused.Snapshot);
        Assert.NotNull(accepted.Snapshot);
    }

    [Fact]
    public void Check_WithoutBudgets_UsesTheConfiguredCeiling()
    {
        var check = new ContractRegistrationCheck(Engine.Real(), Engine.Rules(), Engine.Options(o => o.MaxRuleCost = 10));

        Assert.Contains(check.Check(Request(), []).Findings, f => f.Code == ContractFindingCodes.RuleCostExceeded);
    }

    [Fact]
    public void Check_WithBudgets_RunsTheExamplesUnderTheirIterations()
    {
        var result = Check().Check(Request(), [], ContractBudgets.Default with { MaxRuleIterations = 2 });

        var finding = Assert.Single(result.Findings, f => f.Code == ContractFindingCodes.ExampleFailsRule);
        Assert.Contains("ran past its evaluation budget", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_RefusesMissingOrOutOfRangeBudgets()
    {
        Assert.Throws<ArgumentNullException>(() => Check().Check(Request(), [], null!));
        Assert.Throws<ContractViolationException>(() => Check().Check(Request(), [], ContractBudgets.Default with { MaxRuleCost = 0 }));
    }

    private static ContractValidator Validator() => new(Engine.Real(), Engine.Rules(), Engine.Options());

    private static ContractRegistrationCheck Check() => new(Engine.Real(), Engine.Rules(), Engine.Options());

    private static ContractRegistrationRequest Request() => new()
    {
        ContractRef = "frontier/codes/1.0",
        Schema = Json.Parse(CodesSchema),
        Rules = NoX,
        Provenance = ContractProvenance.Authored,
    };
}
