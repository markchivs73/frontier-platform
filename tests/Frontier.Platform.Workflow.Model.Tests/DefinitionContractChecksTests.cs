using System.Text.Json;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model.Tests.Serialization;

namespace Frontier.Platform.Workflow.Model.Tests;

/// <summary>ADR-E27 D14 and D15: the structural checks on edge projections and embedded contracts.</summary>
public sealed class DefinitionContractChecksTests
{
    [Fact]
    public void Validate_AcceptsADefinitionWithContractsAndAProjection()
    {
        ContractSamples.WorkflowDefinitionWithContracts().Validate();
    }

    [Fact]
    public void ValidateProjections_RefusesAProjectionOnAControlEdge()
    {
        var edge = DataEdge([new ProjectionField { Target = "a", From = "/a" }]) with { Kind = EdgeKind.Control };

        var violation = Assert.Single(WorkflowDefinitionValidator.ValidateProjections([edge]));

        Assert.Equal("Edge 'p' -> 'c' is a control edge; only a data edge may declare a projection.", violation);
    }

    [Fact]
    public void ValidateProjections_IgnoresEdgesWithoutOne()
    {
        Assert.Empty(WorkflowDefinitionValidator.ValidateProjections([DataEdge(null)]));
    }

    [Fact]
    public void ValidateProjections_ReportsBlankAndRepeatedTargets()
    {
        var edge = DataEdge([
            new ProjectionField { Target = "a", From = "/a" },
            new ProjectionField { Target = "a", From = "/b" },
            new ProjectionField { Target = " ", From = "/c" },
        ]);

        Assert.Equal(
            [
                "Edge 'p' -> 'c': projection target 'a' is blank or repeated.",
                "Edge 'p' -> 'c': projection target ' ' is blank or repeated.",
            ],
            WorkflowDefinitionValidator.ValidateProjections([edge]));
    }

    [Theory]
    [InlineData(null, null, "needs a source: set from or literal.")]
    [InlineData("/a", "1", "sets both from and literal; set one.")]
    [InlineData("a", null, "has from 'a', which is not an RFC 6901 JSON Pointer.")]
    [InlineData(null, "null", "has a null literal; omit the field instead.")]
    public void SourceViolation_ExplainsABadSource(string? from, string? literal, string expected)
    {
        var field = new ProjectionField { Target = "t", From = from, Literal = literal is null ? null : Json(literal) };

        Assert.Equal(expected, WorkflowDefinitionValidator.SourceViolation(field));
    }

    [Theory]
    [InlineData("/a", null)]
    [InlineData("", null)]
    [InlineData(null, "{\"x\":1}")]
    [InlineData(null, "false")]
    public void SourceViolation_AcceptsOneGoodSource(string? from, string? literal)
    {
        var field = new ProjectionField { Target = "t", From = from, Literal = literal is null ? null : Json(literal) };

        Assert.Null(WorkflowDefinitionValidator.SourceViolation(field));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/", true)]
    [InlineData("/a/0/b", true)]
    [InlineData("/a~0b/c~1d", true)]
    [InlineData("a", false)]
    [InlineData("/a~", false)]
    [InlineData("/a~2", false)]
    public void IsJsonPointer_FollowsRfc6901(string location, bool expected)
    {
        Assert.Equal(expected, WorkflowDefinitionValidator.IsJsonPointer(location));
    }

    [Fact]
    public void ValidateEmbeddedContracts_AcceptsNoneAndASortedSet()
    {
        Assert.Empty(WorkflowDefinitionValidator.ValidateEmbeddedContracts(null));
        Assert.Empty(WorkflowDefinitionValidator.ValidateEmbeddedContracts([Snapshot("a/x/1.0"), Snapshot("b/x/1.0")]));
    }

    [Fact]
    public void ValidateEmbeddedContracts_RefusesAnUnsortedOrRepeatedSet()
    {
        Assert.Equal(
            [
                "Embedded contracts must be sorted by ref with no repeats; 'a/x/1.0' follows 'b/x/1.0'.",
                "Embedded contracts must be sorted by ref with no repeats; 'a/x/1.0' follows 'a/x/1.0'.",
            ],
            WorkflowDefinitionValidator.ValidateEmbeddedContracts([Snapshot("b/x/1.0"), Snapshot("a/x/1.0"), Snapshot("a/x/1.0")]));
    }

    [Fact]
    public void ValidateEmbeddedContracts_ReportsAMalformedSnapshot()
    {
        var malformed = Snapshot("a/x/1.0") with { Hash = "md5:nope" };

        var violation = Assert.Single(WorkflowDefinitionValidator.ValidateEmbeddedContracts([malformed]));

        Assert.Equal("Embedded contract 'a/x/1.0': hash must be a sha256: content hash", violation);
    }

    [Fact]
    public void Validate_ReportsProjectionAndContractViolationsWithTheRest()
    {
        var definition = ContractSamples.WorkflowDefinitionWithContracts() with
        {
            Contracts = [Snapshot("b/x/1.0"), Snapshot("a/x/1.0")],
            Edges = [DataEdge([new ProjectionField { Target = "t" }]) with { FromNodeId = "get-ticket", ToNodeId = "triage" }],
        };

        var exception = Assert.Throws<ContractViolationException>(definition.Validate);

        Assert.Equal(
            [
                "Edge 'get-ticket' -> 'triage': projection target 't' needs a source: set from or literal.",
                "Embedded contracts must be sorted by ref with no repeats; 'a/x/1.0' follows 'b/x/1.0'.",
            ],
            exception.Violations);
    }

    private static WorkflowEdge DataEdge(IReadOnlyList<ProjectionField>? projection) => new()
    {
        FromNodeId = "p",
        ToNodeId = "c",
        Kind = EdgeKind.Data,
        ContractType = "a/x/1.0",
        Projection = projection,
    };

    private static ContractSnapshot Snapshot(string contractRef) => new()
    {
        ContractRef = contractRef,
        Schema = Json("{}"),
        Rules = [],
        Hash = "sha256:00",
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
