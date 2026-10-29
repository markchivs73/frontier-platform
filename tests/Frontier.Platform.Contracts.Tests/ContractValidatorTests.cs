using System.Text;
using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D3 and D8, against the real JSON Schema engine.</summary>
public sealed class ContractValidatorTests
{
    private static readonly ContractSnapshot Legible = Snapshot(Json.LegibleSchema);

    [Fact]
    public void Validate_AConformingPayload_IsValid() =>
        Assert.True(new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options()).Validate(Legible, Json.Parse("""{ "total": 2 }""")).IsValid);

    [Fact]
    public void Validate_ReportsEachReason_WithoutQuotingThePayload()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options());

        var result = validator.Validate(Legible, Json.Parse("""{ "total": "SECRET-1", "label": "SECRET-LONG-VALUE", "other": "SECRET-3" }"""));

        Assert.False(result.IsValid);
        Assert.Equal(["/total:type", "/label:maxLength", "/other:additionalProperties"], result.Violations.Select(v => $"{v.Location}:{v.Keyword}"));
        Assert.All(result.Violations, v => Assert.DoesNotContain("SECRET", v.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "type": "string", "enum": ["a", "b"] }""", "\"SECRET\"")]
    [InlineData("""{ "type": "string", "const": "a" }""", "\"SECRET\"")]
    [InlineData("""{ "type": "string", "pattern": "^a" }""", "\"SECRET\"")]
    [InlineData("""{ "type": "number", "maximum": 1 }""", "123456789")]
    public void Validate_MessagesQuoteTheContract_NeverThePayload(string schema, string payload)
    {
        var result = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options()).Validate(Snapshot(schema), Json.Parse(payload));

        var violation = Assert.Single(result.Violations);
        Assert.DoesNotContain("SECRET", violation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_APatternThatRunsPastItsTimeLimit_IsAPermanentViolation()
    {
        var engine = new CorvusSchemaEngine(Engine.Options(o => o.RegexMatchTimeoutMilliseconds = 1));
        var validator = new ContractValidator(engine, Engine.Rules(), Engine.Options());
        var payload = Json.Parse("\"" + new string('a', 5_000) + "!\"");

        var result = validator.Validate(Snapshot("""{ "type": "string", "pattern": "^(a|aa)*$" }"""), payload);

        Assert.Equal([SchemaViolations.PatternTimeout], result.Violations);
        Assert.Equal(("pattern", ""), (SchemaViolations.PatternTimeout.Keyword, SchemaViolations.PatternTimeout.Location));
    }

    [Fact]
    public void Validate_RunsTheRules_OnceTheSchemaPasses()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options());
        var contract = Snapshot(Json.LegibleSchema, new ContractRule { Rule = "self.total < 10.0", Message = "Under ten.", FieldPath = "/total" });

        Assert.True(validator.Validate(contract, Json.Parse("""{ "total": 9 }""")).IsValid);
        Assert.Equal([new ContractViolation("/total", "rule", "Under ten.")], validator.Validate(contract, Json.Parse("""{ "total": 11 }""")).Violations);
        Assert.Equal(["type"], validator.Validate(contract, Json.Parse("""{ "total": "x" }""")).Violations.Select(v => v.Keyword));
    }

    [Fact]
    public void Validate_APinnedContractWhoseRuleDoesNotCompile_IsAPermanentViolation()
    {
        var contract = Snapshot(Json.LegibleSchema, new ContractRule { Rule = "self.nope", Message = "m" });

        var exception = Assert.Throws<ContractViolationException>(() => new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options()).Validate(contract, Json.Parse("""{ "total": 1 }""")));
        Assert.Contains("a rule does not compile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AssertsNoFormat_SinceFormatIsAnAnnotation() =>
        Assert.True(new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options()).Validate(Snapshot("""{ "type": "string", "format": "email" }"""), Json.Parse("\"not an email\"")).IsValid);

    [Fact]
    public void Validate_StopsAtMaxViolations()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options(o => o.MaxViolations = 1));

        Assert.Single(validator.Validate(Legible, Json.Parse("""{ "total": "x", "other": 1 }""")).Violations);
    }

    [Fact]
    public void Validate_Bytes_ParsesAndValidates()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options());

        Assert.True(validator.Validate(Legible, Encoding.UTF8.GetBytes("""{ "total": 1 }""")).IsValid);
        Assert.False(validator.Validate(Legible, Encoding.UTF8.GetBytes("""{ "total": -1 }""")).IsValid);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[[[[1]]]]")]
    public void Validate_Bytes_ThatAreNotJsonOrTooDeep_AreAViolation(string payload)
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options(o => o.MaxPayloadDepth = 3));

        var violation = Assert.Single(validator.Validate(Snapshot("{}"), Encoding.UTF8.GetBytes(payload)).Violations);
        Assert.Equal(("", "json"), (violation.Location, violation.Keyword));
        Assert.Contains("3 levels", violation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[[[[1]]]]", false)]
    [InlineData("""{ "a": { "b": [1] } }""", true)]
    [InlineData("7", true)]
    public void Validate_Element_ChecksDepth(string payload, bool valid) =>
        Assert.Equal(valid, new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options(o => o.MaxPayloadDepth = 3)).Validate(Snapshot("{}"), Json.Parse(payload)).IsValid);

    [Fact]
    public void Validate_ASnapshotWhoseContentNoLongerMatchesItsHash_IsRefused()
    {
        var tampered = Legible with { Schema = Json.Parse("""{ "type": "object" }""") };

        var exception = Assert.Throws<ContractViolationException>(() => new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options()).Validate(tampered, Json.Parse("{}")));
        Assert.Contains("hash does not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_CompilesEachContractOnce()
    {
        var engine = new CountingEngine(Engine.Real());
        var validator = new ContractValidator(engine, Engine.Rules(), Engine.Options());

        validator.Validate(Legible, Json.Parse("""{ "total": 1 }"""));
        validator.Validate(Legible, Json.Parse("""{ "total": 2 }"""));
        validator.Validate(Snapshot("{}"), Json.Parse("1"));

        Assert.Equal(2, engine.Compilations);
    }

    [Fact]
    public void Validate_APinnedContractThatDoesNotCompile_IsAPermanentViolation()
    {
        var validator = new ContractValidator(new CountingEngine(Engine.Real(), refuseWith: "bad keyword"), Engine.Rules(), Engine.Options());

        var exception = Assert.Throws<ContractViolationException>(() => validator.Validate(Legible, Json.Parse("{}")));
        Assert.Contains("bad keyword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TwoContractsWithTheSameId_AreKeptApart()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options());
        var integers = Snapshot("""{ "$id": "urn:same", "type": "integer" }""");
        var strings = Snapshot("""{ "$id": "urn:same", "type": "string" }""");

        Assert.True(validator.Validate(integers, Json.Parse("1")).IsValid);
        Assert.True(validator.Validate(strings, Json.Parse("\"a\"")).IsValid);
        Assert.False(validator.Validate(integers, Json.Parse("\"a\"")).IsValid);
    }

    [Fact]
    public void Validate_NullContract_Throws()
    {
        var validator = new ContractValidator(Engine.Real(), Engine.Rules(), Engine.Options());

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, Json.Parse("{}")));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, ReadOnlyMemory<byte>.Empty));
    }

    internal static ContractSnapshot Snapshot(string schema, params ContractRule[] rules)
    {
        var element = Json.Parse(schema);
        return new ContractSnapshot { ContractRef = "frontier/test/1.0", Schema = element, Rules = rules, Hash = ContractHash.Compute(element, rules) };
    }
}
