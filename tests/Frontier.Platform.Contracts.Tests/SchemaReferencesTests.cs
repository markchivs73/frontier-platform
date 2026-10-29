namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D3 and D9: 2020-12 only, and no reference leaves the contract.</summary>
public sealed class SchemaReferencesTests
{
    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "$schema": "https://json-schema.org/draft/2020-12/schema" }""")]
    [InlineData("""{ "$schema": "https://json-schema.org/draft/2020-12/schema#" }""")]
    [InlineData("""{ "$ref": "#/$defs/a", "$defs": { "a": { "$dynamicRef": "#node" } } }""")]
    public void Findings_ForA2020_12SchemaWithLocalReferences_AreNone(string json) =>
        Assert.Empty(SchemaReferences.Findings(Json.Parse(json)));

    [Theory]
    [InlineData("""{ "$schema": "http://json-schema.org/draft-07/schema#" }""")]
    [InlineData("""{ "$schema": 7 }""")]
    public void Findings_ForAnotherDialect_AreAnError(string json)
    {
        var finding = Assert.Single(SchemaReferences.Findings(Json.Parse(json)));

        Assert.Equal((ContractFindingCodes.DialectUnsupported, ContractFindingSeverity.Error, "/schema/$schema"), (finding.Code, finding.Severity, finding.Location));
    }

    [Fact]
    public void Findings_ForReferencesOutsideTheContract_NameEachOne()
    {
        var schema = Json.Parse("""
            {
              "properties": { "a": { "$ref": "https://example.com/s.json" } },
              "$defs": { "b": { "$dynamicRef": "file:///etc/passwd" }, "c": { "$ref": 1 } }
            }
            """);

        Assert.Equal(
            ["/schema/properties/a/$ref", "/schema/$defs/b/$dynamicRef", "/schema/$defs/c/$ref"],
            SchemaReferences.Findings(schema).Select(f => f.Location));
    }
}
