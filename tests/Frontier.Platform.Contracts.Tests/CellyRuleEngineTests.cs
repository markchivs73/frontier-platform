using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D1 and D2 against the real CEL engine.</summary>
public sealed class CellyRuleEngineTests
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "start": { "type": "number" },
            "end": { "type": "number" },
            "name": { "type": "string", "maxLength": 20 },
            "note": { "type": "string" },
            "tags": { "type": "array", "maxItems": 5, "items": { "type": "string", "maxLength": 10 } },
            "loose": { "type": "array", "items": { "type": "string" } },
            "lines": { "type": "array", "maxItems": 50, "items": { "type": "object", "properties": { "qty": { "type": "number" }, "codes": { "type": "array", "maxItems": 50, "items": { "type": "string", "maxLength": 5 } } } } },
            "m": { "type": "object", "maxProperties": 4, "propertyNames": { "maxLength": 8 }, "additionalProperties": { "type": "number" } },
            "any": { "oneOf": [ { "type": "string" }, { "type": "number" } ] },
            "when": { "type": "string", "maxLength": 40 }
          }
        }
        """;

    [Fact]
    public void Evaluate_ATrueRule_Passes_AndAFalseOne_ReportsItsMessageAtItsFieldPath()
    {
        var rules = Compile(Rule("self.end >= self.start", "End is not before start.", "/end"), Rule("size(self.name) > 0", "A name is required."));

        Assert.Empty(rules.Evaluate(Json.Parse("""{ "start": 1, "end": 2, "name": "a" }"""), 10));
        Assert.Equal(
            [new ContractViolation("/end", "rule", "End is not before start."), new ContractViolation("", "rule", "A name is required.")],
            rules.Evaluate(Json.Parse("""{ "start": 3, "end": 2, "name": "" }"""), 10));
    }

    [Fact]
    public void Evaluate_StopsAtMaxViolations() =>
        Assert.Single(Compile(Rule("false", "One."), Rule("false", "Two.")).Evaluate(Json.Parse("{}"), 1));

    [Fact]
    public void Evaluate_NoRules_IsAlwaysEmpty() => Assert.Empty(Compile().Evaluate(Json.Parse("{}"), 10));

    [Fact]
    public void Evaluate_AnAbsentField_IsAViolation_WhileHasIsTheGuard()
    {
        var rules = Compile(Rule("self.name != ''", "A name is required."), Rule("!has(self.note) || size(self.note) < 0", "Never."));

        var violations = rules.Evaluate(Json.Parse("{}"), 10);

        Assert.Equal(["A name is required. (the rule could not be evaluated)"], violations.Select(v => v.Message));
    }

    [Fact]
    public void Evaluate_ARuleWithAnUntypedResultThatIsNotBoolean_IsAViolation() =>
        Assert.Equal(["Must be true. (the rule could not be evaluated)"], Compile(Rule("self.any", "Must be true.")).Evaluate(Json.Parse("""{ "any": "x" }"""), 10).Select(v => v.Message));

    [Fact]
    public void Evaluate_IteratesMapsInKeyOrder_WhateverOrderThePayloadUsed()
    {
        var rules = Compile(Rule("self.m.map(k, self.m[k])[0] == 2.0", "The first key is a, whose value is 2."));

        Assert.Empty(rules.Evaluate(Json.Parse("""{ "m": { "c": 1, "a": 2, "b": 3 } }"""), 10));
    }

    [Fact]
    public void Evaluate_ARuleThatRunsPastItsIterationBudget_IsAViolation()
    {
        var rules = Compile(o => o.MaxRuleIterations = 10, Rule("self.lines.all(l, l.codes.all(c, c != 'x'))", "No x."));
        var lines = string.Join(",", Enumerable.Repeat("""{ "codes": ["a", "b", "c", "d"] }""", 5));

        var violation = Assert.Single(rules.Evaluate(Json.Parse($$"""{ "lines": [{{lines}}] }"""), 10));

        Assert.Equal("No x. (the rule ran past its evaluation budget)", violation.Message);
    }

    [Theory]
    [InlineData("self.end >=", "does not parse")]
    [InlineData("self.missing > 0.0", "does not type-check")]
    [InlineData("self.start + 1 > 0.0", "does not type-check")]
    public void Compile_ARuleThatIsNotValidCel_IsRefused(string expression, string reason)
    {
        var finding = Assert.Single(Refusals(expression));

        Assert.Equal((ContractFindingCodes.RuleInvalid, "/rules/0", ContractFindingSeverity.Error), (finding.Code, finding.Location, finding.Severity));
        Assert.Contains(reason, finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ANullExpression_IsRefused() =>
        Assert.Equal(ContractFindingCodes.RuleInvalid, Assert.Single(Refusals(null!)).Code);

    [Theory]
    [InlineData("self.name.matches('^a')")]
    [InlineData("matches(self.name, '^a')")]
    [InlineData("timestamp(self.when).getHours('Europe/London') > 1")]
    public void Compile_ARuleOutsideTheProfile_IsRefused(string expression) =>
        Assert.Equal(ContractFindingCodes.RuleFunctionNotAllowed, Assert.Single(Refusals(expression)).Code);

    [Fact]
    public void Compile_ATimestampAccessorInUtc_IsAllowed() =>
        Assert.Empty(Refusals("timestamp(self.when).getHours() >= 0"));

    [Fact]
    public void Compile_ARuleThatIsNotBoolean_IsRefused() =>
        Assert.Equal(ContractFindingCodes.RuleNotBoolean, Assert.Single(Refusals("self.name")).Code);

    [Theory]
    [InlineData("self.loose.all(t, t == 'a')")]
    [InlineData("self.note == 'a'")]
    public void Compile_ARuleWhoseCostTheSchemaDoesNotBound_IsRefused(string expression) =>
        Assert.Equal(ContractFindingCodes.RuleCostUnbounded, Assert.Single(Refusals(expression)).Code);

    [Theory]
    [InlineData("self.tags.all(t, t == 'a')")]
    [InlineData("self.lines.all(l, l.codes.exists(c, c == 'x') || l.qty > 0.0)")]
    [InlineData("self.m.all(k, size(k) < 9)")]
    public void Compile_ARuleOverBoundedCollections_IsAccepted(string expression) => Assert.Empty(Refusals(expression));

    [Fact]
    public void Compile_ARuleOverTheCostCeiling_IsRefused()
    {
        var finding = Assert.Single(new CellyRuleEngine(Engine.Options(o => o.MaxRuleCost = 10)).Compile(Json.Parse(Schema), [Rule("self.lines.all(l, l.codes.all(c, c != 'x'))", "No x.")]).Findings);

        Assert.Equal(ContractFindingCodes.RuleCostExceeded, finding.Code);
        Assert.Contains("over the limit of 10", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ReportsEveryBadRule_AndCompilesNone()
    {
        var compilation = new CellyRuleEngine(Engine.Options()).Compile(Json.Parse(Schema), [Rule("true", "ok"), Rule("self.x", "bad"), Rule("1", "bad")]);

        Assert.Null(compilation.Rules);
        Assert.Equal(["/rules/1", "/rules/2"], compilation.Findings.Select(f => f.Location));
    }

    internal static ContractRule Rule(string expression, string message, string? fieldPath = null) => new() { Rule = expression, Message = message, FieldPath = fieldPath };

    private static ICompiledRules Compile(params ContractRule[] rules) => Compile(null, rules);

    private static ICompiledRules Compile(Action<ContractsOptions>? configure, params ContractRule[] rules)
    {
        var compilation = new CellyRuleEngine(Engine.Options(configure)).Compile(Json.Parse(Schema), rules);
        Assert.Empty(compilation.Findings);
        return compilation.Rules!;
    }

    private static IReadOnlyList<ContractFinding> Refusals(string expression) =>
        new CellyRuleEngine(Engine.Options()).Compile(Json.Parse(Schema), [Rule(expression, "m")]).Findings;
}
