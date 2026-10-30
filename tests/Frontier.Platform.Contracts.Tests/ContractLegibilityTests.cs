using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-PA41: the lint and the property descriptions, read from a registered snapshot.</summary>
public sealed class ContractLegibilityTests
{
    private static ContractSnapshot Snapshot(string schema, params ContractRule[] rules)
    {
        var element = Json.Parse(schema);
        return new ContractSnapshot { ContractRef = "acme/thing/1.0", Schema = element, Rules = rules, Hash = ContractHash.Compute(element, rules) };
    }

    [Fact]
    public void Gaps_ALegibleContract_HasNone() =>
        Assert.Empty(ContractLegibility.Gaps(Snapshot(Json.LegibleSchema, new ContractRule { Rule = "true", Message = "Always." })));

    [Fact]
    public void Gaps_AreTheLintsFindings_AsWarnings()
    {
        var snapshot = Snapshot("""{ "type": "object", "properties": { "a": { "type": "string" } } }""", new ContractRule { Rule = "true", Message = " " });

        var gaps = ContractLegibility.Gaps(snapshot);

        Assert.Equal(
            ContractLint.Findings(snapshot.Schema, snapshot.Rules, ContractProvenance.Authored).Select(f => (f.Code, f.Location, f.Message)),
            gaps.Select(f => (f.Code, f.Location, f.Message)));
        Assert.Equal(5, gaps.Count);
        Assert.All(gaps, gap => Assert.Equal(ContractFindingSeverity.Warning, gap.Severity));
    }

    [Fact]
    public void Properties_ListEveryPropertyAtAnyDepth_WithItsDescriptionOrNull()
    {
        var snapshot = Snapshot("""
            {
              "type": "object",
              "properties": {
                "total": { "description": "How many.", "type": "integer" },
                "lines": { "type": "array", "items": { "type": "object", "properties": { "sku": { "description": "  " }, "a/b": true } } }
              },
              "examples": [ { "description": "not a property" } ]
            }
            """);

        Assert.Equal(
            [
                new ContractPropertyAnnotation("/schema/properties/total", "How many."),
                new ContractPropertyAnnotation("/schema/properties/lines", null),
                new ContractPropertyAnnotation("/schema/properties/lines/items/properties/sku", null),
                new ContractPropertyAnnotation("/schema/properties/lines/items/properties/a~1b", null),
            ],
            ContractLegibility.Properties(snapshot));
    }

    [Theory]
    [InlineData("""{ "description": "What it is for." }""", "What it is for.")]
    [InlineData("""{ "description": "" }""", null)]
    [InlineData("""{ "description": 3 }""", null)]
    [InlineData("""{ "type": "object" }""", null)]
    public void Description_IsTheNonBlankContractLevelText(string schema, string? expected) =>
        Assert.Equal(expected, ContractLegibility.Description(Snapshot(schema)));

    [Fact]
    public void Description_OfABooleanPropertySchema_IsNull() =>
        Assert.Null(Assert.Single(ContractLegibility.Properties(Snapshot("""{ "properties": { "x": false } }"""))).Description);

    [Fact]
    public void EachMember_RefusesANullSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => ContractLegibility.Gaps(null!));
        Assert.Throws<ArgumentNullException>(() => ContractLegibility.Properties(null!));
        Assert.Throws<ArgumentNullException>(() => ContractLegibility.Description(null!));
    }
}
