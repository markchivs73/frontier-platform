using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D10 (legibility) and D4 (closed content model, which must be stated).</summary>
public sealed class ContractLintTests
{
    [Fact]
    public void Findings_ALegibleContract_HasNone() =>
        Assert.Empty(ContractLint.Findings(Json.Parse(Json.LegibleSchema), [new() { Rule = "true", Message = "Always." }], ContractProvenance.Authored));

    [Fact]
    public void Findings_ReportEachGap_AtItsLocation()
    {
        var schema = Json.Parse("""
            {
              "type": "object",
              "properties": { "a": { "type": "string" }, "b": true, "c": { "description": "  " } },
              "examples": []
            }
            """);
        ContractRule[] rules = [new() { Rule = "true", Message = "ok" }, new() { Rule = "false", Message = " " }];

        var findings = ContractLint.Findings(schema, rules, ContractProvenance.Generated);

        Assert.Equal(
            [
                (ContractFindingCodes.DescriptionMissing, "/schema/description"),
                (ContractFindingCodes.ExamplesMissing, "/schema/examples"),
                (ContractFindingCodes.ContentModelUnstated, "/schema"),
                (ContractFindingCodes.PropertyDescriptionMissing, "/schema/properties/a"),
                (ContractFindingCodes.PropertyDescriptionMissing, "/schema/properties/b"),
                (ContractFindingCodes.PropertyDescriptionMissing, "/schema/properties/c"),
                (ContractFindingCodes.RuleMessageMissing, "/rules/1"),
            ],
            findings.Select(f => (f.Code, f.Location)));
        Assert.All(findings, f => Assert.Equal(ContractFindingSeverity.Error, f.Severity));
    }

    [Fact]
    public void Findings_ForADiscoveredContract_AreWarnings()
    {
        var findings = ContractLint.Findings(Json.Parse("""{ "type": "object" }"""), [], ContractProvenance.Discovered);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(ContractFindingSeverity.Warning, f.Severity));
    }

    [Theory]
    [InlineData("""{ "type": "object", "additionalProperties": true }""")]
    [InlineData("""{ "properties": {}, "unevaluatedProperties": false }""")]
    [InlineData("""{ "type": "string" }""")]
    [InlineData("""{ "type": ["string", "null"] }""")]
    [InlineData("""{ "type": 3 }""")]
    [InlineData("""{ "unevaluatedProperties": false, "allOf": [ { "type": "object", "properties": {} } ] }""")]
    public void ContentModel_IsStated_OrNotAnObject_OrStatedByTheComposingSchema(string json) =>
        Assert.DoesNotContain(ContractLint.Findings(Json.Parse(json), [], ContractProvenance.Authored), f => f.Code == ContractFindingCodes.ContentModelUnstated);

    [Theory]
    [InlineData("""{ "type": ["object", "null"] }""")]
    [InlineData("""{ "properties": {} }""")]
    [InlineData("""{ "additionalProperties": false, "$defs": { "inner": { "type": "object" } } }""")]
    public void ContentModel_Unstated_OnAnyObjectSchema_IsReported(string json) =>
        Assert.Contains(ContractLint.Findings(Json.Parse(json), [], ContractProvenance.Authored), f => f.Code == ContractFindingCodes.ContentModelUnstated);

    [Fact]
    public void DescribesObject_ABooleanSchema_IsFalse() => Assert.False(ContractLint.DescribesObject(Json.Parse("true")));

    [Fact]
    public void Examples_ThatAreNotAnArray_CountAsMissing() =>
        Assert.Contains(ContractLint.Findings(Json.Parse("""{ "examples": {} }"""), [], ContractProvenance.Authored), f => f.Code == ContractFindingCodes.ExamplesMissing);

    [Fact]
    public void Description_ThatIsNotAString_CountsAsMissing() =>
        Assert.Contains(ContractLint.Findings(Json.Parse("""{ "description": 1 }"""), [], ContractProvenance.Authored), f => f.Code == ContractFindingCodes.DescriptionMissing);
}
