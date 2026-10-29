using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D4, D5, D10: the whole registration check, against the real engine.</summary>
public sealed class ContractRegistrationCheckTests
{
    private static readonly ContractRule[] Rules = [new() { Rule = "self.total >= 0.0", Message = "Never negative." }];

    [Fact]
    public void Check_ALegibleContract_IsAccepted_AsAHashedSnapshot()
    {
        var result = Check(Request(Json.LegibleSchema));

        Assert.True(result.IsAccepted);
        Assert.Empty(result.Findings);
        var snapshot = result.Snapshot!;
        Assert.Equal("frontier/evidence-summary/1.0", snapshot.ContractRef);
        Assert.Equal(Rules, snapshot.Rules);
        Assert.Equal(ContractHash.Compute(Json.Parse(Json.LegibleSchema), Rules), snapshot.Hash);
        snapshot.Validate();
    }

    [Fact]
    public void Check_AMalformedRef_IsRefused_AndStillLintsTheSchema()
    {
        var result = Check(Request(Json.LegibleSchema) with { ContractRef = "evidence" });

        Assert.False(result.IsAccepted);
        Assert.Equal([ContractFindingCodes.RefMalformed], result.Findings.Select(f => f.Code));
    }

    [Fact]
    public void Check_ASchemaThatIsNotAnObject_IsRefusedAtOnce()
    {
        var result = Check(Request("true"));

        Assert.Equal([ContractFindingCodes.SchemaNotObject], result.Findings.Select(f => f.Code));
    }

    [Fact]
    public void Check_AReferenceOutsideTheContract_IsRefused_WithoutCompiling()
    {
        var engine = new CountingEngine(Engine.Real());
        var schema = Json.LegibleSchema.Replace("\"type\": \"integer\", \"minimum\": 0", "\"$ref\": \"https://example.com/total.json\"", StringComparison.Ordinal);

        var result = new ContractRegistrationCheck(engine).Check(Request(schema), []);

        Assert.Equal([ContractFindingCodes.RefNotLocal], result.Findings.Select(f => f.Code));
        Assert.Equal(0, engine.Compilations);
    }

    [Fact]
    public void Check_ASchemaThatBreaksTheMetaSchema_IsRefused_AtTheBadKeyword()
    {
        var engine = new CountingEngine(Engine.Real());
        var schema = Json.LegibleSchema.Replace("\"minimum\": 0", "\"minimum\": \"zero\"", StringComparison.Ordinal);

        var finding = Assert.Single(new ContractRegistrationCheck(engine).Check(Request(schema), []).Findings);

        Assert.Equal((ContractFindingCodes.SchemaInvalid, ContractFindingSeverity.Error, "/schema/properties/total/minimum"), (finding.Code, finding.Severity, finding.Location));
        Assert.StartsWith("This is not valid JSON Schema 2020-12: ", finding.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.Compilations);
    }

    [Fact]
    public void Check_ASchemaTheEngineCannotCompile_IsRefused()
    {
        var finding = Assert.Single(new ContractRegistrationCheck(new CountingEngine(Engine.Real(), refuseWith: "unsupported")).Check(Request(Json.LegibleSchema), []).Findings);

        Assert.Equal((ContractFindingCodes.SchemaInvalid, "/schema", "The schema does not compile: unsupported"), (finding.Code, finding.Location, finding.Message));
    }

    [Fact]
    public void Check_AnExampleThatDoesNotSatisfyTheSchema_IsRefused_NamingWhy()
    {
        var schema = Json.LegibleSchema.Replace("""{ "total": 3, "label": "ok" }""", """{ "total": 3 }, { "total": -1 }""", StringComparison.Ordinal);

        var finding = Assert.Single(Check(Request(schema)).Findings);

        Assert.Equal((ContractFindingCodes.ExampleInvalid, "/schema/examples/1"), (finding.Code, finding.Location));
        Assert.Contains("at '/total'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_ADiscoveredContract_WithLintGaps_IsAccepted_WithWarnings()
    {
        var result = Check(Request("""{ "type": "object", "properties": { "a": { "type": "string" } }, "examples": [ { "a": 1 } ] }""") with { Provenance = ContractProvenance.Discovered, Rules = [] });

        Assert.True(result.IsAccepted);
        Assert.Equal(
            [ContractFindingCodes.DescriptionMissing, ContractFindingCodes.ContentModelUnstated, ContractFindingCodes.PropertyDescriptionMissing, ContractFindingCodes.ExampleInvalid],
            result.Findings.Select(f => f.Code));
        Assert.All(result.Findings, f => Assert.Equal(ContractFindingSeverity.Warning, f.Severity));
    }

    [Fact]
    public void Check_ListsErrorsBeforeWarnings()
    {
        var result = Check(Request("""{ "$schema": "http://json-schema.org/draft-07/schema#", "type": "object" }""") with { Provenance = ContractProvenance.Discovered });

        Assert.Equal([ContractFindingSeverity.Error, ContractFindingSeverity.Warning, ContractFindingSeverity.Warning, ContractFindingSeverity.Warning],
            result.Findings.Select(f => f.Severity));
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void Check_AppliesCompatibility_AgainstTheRegisteredMinors()
    {
        var earlier = Check(Request(Json.LegibleSchema)).Snapshot!;

        var result = Check(Request(Json.LegibleSchema) with { ContractRef = "frontier/evidence-summary/1.1", Rules = [.. Rules, new() { Rule = "true", Message = "Also." }] }, [earlier]);

        Assert.Equal([ContractFindingCodes.MinorChangesRules], result.Findings.Select(f => f.Code));
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "examples": { } }""")]
    public void ExampleFindings_WithNoExamplesArray_AreNone_SinceTheLintReportsTheGap(string schema) =>
        Assert.Empty(ContractRegistrationCheck.ExampleFindings(Json.Parse(schema), null!, ContractFindingSeverity.Error));

    [Fact]
    public void Check_NullRules_AreTreatedAsNone()
    {
        var result = Check(Request(Json.LegibleSchema) with { Rules = null! });

        Assert.True(result.IsAccepted);
        Assert.Empty(result.Snapshot!.Rules);
    }

    [Fact]
    public void Check_AcceptedSnapshot_DoesNotShareTheCallersDocument()
    {
        ContractSnapshot snapshot;
        using (var document = JsonDocument.Parse(Json.LegibleSchema))
        {
            snapshot = new ContractRegistrationCheck(Engine.Real()).Check(Request(Json.LegibleSchema) with { Schema = document.RootElement }, []).Snapshot!;
        }

        Assert.Equal(JsonValueKind.Object, snapshot.Schema.ValueKind);
    }

    [Fact]
    public void Check_NullArguments_Throw()
    {
        var check = new ContractRegistrationCheck(Engine.Real());

        Assert.Throws<ArgumentNullException>(() => check.Check(null!, []));
        Assert.Throws<ArgumentNullException>(() => check.Check(Request(Json.LegibleSchema), null!));
    }

    private static ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<ContractSnapshot>? registered = null) =>
        new ContractRegistrationCheck(Engine.Real()).Check(request, registered ?? []);

    private static ContractRegistrationRequest Request(string schema) => new()
    {
        ContractRef = "frontier/evidence-summary/1.0",
        Schema = Json.Parse(schema),
        Rules = Rules,
        Provenance = ContractProvenance.Authored,
    };
}
