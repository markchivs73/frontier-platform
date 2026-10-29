using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D5, strict and sound (Mark, 2026-10-29).</summary>
public sealed class ContractCompatibilityTests
{
    private const string Earlier = """
        { "type": "object", "description": "v1", "properties": { "total": { "type": "number", "description": "n", "format": "x" } }, "additionalProperties": false }
        """;

    private static readonly ContractRule KeepRule = new() { Rule = "self.total >= 0.0", Message = "Never negative." };
    private static readonly ContractRule DropRule = new() { Rule = "self.total < 1000.0", Message = "Small." };

    [Fact]
    public void AMinor_ChangingOnlyAnnotations_RemovingARule_AndRewordingOne_IsCompatible()
    {
        var schema = Json.Parse("""
            { "type": "object", "description": "v2", "title": "T", "examples": [], "$comment": "c", "properties": { "total": { "type": "number", "description": "changed", "format": "y", "default": 1, "deprecated": true, "readOnly": true, "writeOnly": false, "contentMediaType": "a", "contentEncoding": "b", "contentSchema": {} } }, "additionalProperties": false }
            """);

        Assert.Empty(Findings("frontier/x/1.1", schema, [KeepRule with { Message = "Reworded.", FieldPath = "/total" }], Registered("frontier/x/1.0", Earlier, [KeepRule, DropRule])));
    }

    [Theory]
    [InlineData("""{ "type": "object", "properties": { "total": { "type": "integer" } }, "additionalProperties": false }""")]
    [InlineData("""{ "type": "object", "properties": { "total": { "type": "number" }, "extra": { "type": "string" } }, "additionalProperties": false }""")]
    [InlineData("""{ "type": "object", "properties": { "total": { "type": "number" } }, "additionalProperties": true }""")]
    public void AMinor_ChangingStructure_NeedsANewMajor(string json)
    {
        var finding = Assert.Single(Findings("frontier/x/1.1", Json.Parse(json), [], Registered("frontier/x/1.0", Earlier, [])));

        Assert.Equal((ContractFindingCodes.MinorChangesSchema, "/schema"), (finding.Code, finding.Location));
    }

    [Fact]
    public void APropertyNamedLikeAnAnnotation_IsStructure_NotAnnotation()
    {
        const string withDescriptionProperty = """{ "properties": { "description": { "type": "string" } } }""";
        const string withoutIt = """{ "properties": { } }""";

        Assert.Single(Findings("frontier/x/1.1", Json.Parse(withoutIt), [], Registered("frontier/x/1.0", withDescriptionProperty, [])));
    }

    [Fact]
    public void AMinor_AddingOrChangingARule_NeedsANewMajor()
    {
        var findings = Findings("frontier/x/1.1", Json.Parse(Earlier), [KeepRule, KeepRule with { Rule = "self.total >= 1.0" }], Registered("frontier/x/1.0", Earlier, [KeepRule]));

        var finding = Assert.Single(findings);
        Assert.Equal((ContractFindingCodes.MinorChangesRules, "/rules/1"), (finding.Code, finding.Location));
    }

    [Fact]
    public void EveryEarlierMinor_IsChecked_NotOnlyTheLatest()
    {
        var registered = Registered("frontier/x/1.0", Earlier, []).Concat(Registered("frontier/x/1.1", Earlier, [KeepRule])).ToList();

        var findings = Findings("frontier/x/1.2", Json.Parse(Earlier), [KeepRule], registered);

        Assert.Equal([ContractFindingCodes.MinorChangesRules], findings.Select(f => f.Code));
    }

    [Fact]
    public void AnExistingVersion_IsRefused_AndPointsAtTheNextMinor()
    {
        var registered = Registered("frontier/x/1.0", Earlier, []).Concat(Registered("frontier/x/1.1", Earlier, [])).ToList();

        var finding = Assert.Single(Findings("frontier/x/1.0", Json.Parse(Earlier), [], registered));

        Assert.Equal(ContractFindingCodes.VersionExists, finding.Code);
        Assert.Contains("frontier/x/1.2", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMinorBelowTheLatest_IsRefused()
    {
        var registered = Registered("frontier/x/1.0", Earlier, []).Concat(Registered("frontier/x/1.3", Earlier, [])).ToList();

        var finding = Assert.Single(Findings("frontier/x/1.2", Json.Parse(Earlier), [], registered));

        Assert.Equal(ContractFindingCodes.VersionNotNewer, finding.Code);
    }

    [Fact]
    public void OtherIds_OtherMajors_AndUnparseableRefs_AreIgnored()
    {
        var registered = Registered("frontier/y/1.0", "{}", [])
            .Concat(Registered("frontier/x/2.0", "{}", []))
            .Concat(Registered("not a ref", "{}", []))
            .ToList();

        Assert.Empty(Findings("frontier/x/1.0", Json.Parse(Earlier), [DropRule], registered));
    }

    [Fact]
    public void AnnotationsAreStripped_InsideEveryKindOfApplicator()
    {
        const string earlier = """
            { "allOf": [ { "description": "a", "type": "object" } ], "$defs": { "d": { "title": "t", "type": "string" } }, "not": { "examples": [1], "const": 2 }, "anyOf": "odd", "required": ["x"] }
            """;
        const string later = """
            { "allOf": [ { "type": "object" } ], "$defs": { "d": { "type": "string" } }, "not": { "const": 2 }, "anyOf": "odd", "required": ["x"] }
            """;

        Assert.Equal(ContractCompatibility.Structure(Json.Parse(earlier)), ContractCompatibility.Structure(Json.Parse(later)));
        Assert.NotEqual(ContractCompatibility.Structure(Json.Parse(earlier)), ContractCompatibility.Structure(Json.Parse(later.Replace("\"x\"", "\"y\"", StringComparison.Ordinal))));
    }

    private static IReadOnlyList<ContractFinding> Findings(string candidate, JsonElement schema, IReadOnlyList<ContractRule> rules, IReadOnlyList<ContractSnapshot> registered)
    {
        Assert.True(ContractRef.TryParse(candidate, out var parsed));
        return ContractCompatibility.Findings(parsed!, schema, rules, registered);
    }

    private static List<ContractSnapshot> Registered(string contractRef, string schema, IReadOnlyList<ContractRule> rules)
    {
        var element = Json.Parse(schema);
        return [new ContractSnapshot { ContractRef = contractRef, Schema = element, Rules = rules, Hash = ContractHash.Compute(element, rules) }];
    }
}
