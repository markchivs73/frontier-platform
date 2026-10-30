using System.Text.Json;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>ADR-E27 D14: the shape questions an edge projection asks of the contracts at either end.</summary>
public sealed class ContractShapesTests
{
    private static readonly JsonElement Ticket = Parse("""
        {
          "type": "object",
          "properties": {
            "id": { "type": "string", "description": "Id." },
            "owner": { "$ref": "#/$defs/person" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "meta": { "type": "object", "additionalProperties": { "type": "integer" } },
            "a/b": { "const": 1 }
          },
          "required": ["id", "owner"],
          "additionalProperties": false,
          "$defs": {
            "person": { "type": "object", "description": "A person.", "properties": { "name": { "type": "string" } }, "required": ["name"], "additionalProperties": false }
          }
        }
        """);

    [Theory]
    [InlineData("/id", """{"type":"string","description":"Id."}""")]
    [InlineData("/owner/name", """{"type":"string"}""")]
    [InlineData("/tags/3", """{"type":"string"}""")]
    [InlineData("/meta/anything", """{"type":"integer"}""")]
    [InlineData("/a~1b", """{"const":1}""")]
    public void ValueSchema_FindsTheSchemaOfTheValueAtAnInstancePointer(string location, string expected)
    {
        Assert.Equal(expected, ContractShapes.ValueSchema(Ticket, location)!.Value.GetRawText());
    }

    [Fact]
    public void ValueSchema_InlinesReferences_AndDropsDefs()
    {
        var owner = ContractShapes.ValueSchema(Ticket, "/owner")!.Value;
        var whole = ContractShapes.ValueSchema(Ticket, "")!.Value;

        Assert.Equal("""{"type":"object","description":"A person.","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""", owner.GetRawText());
        Assert.False(whole.TryGetProperty("$defs", out _));
        Assert.Equal(owner.GetRawText(), whole.GetProperty("properties").GetProperty("owner").GetRawText());
    }

    [Theory]
    [InlineData("/nope")]
    [InlineData("/id/deeper")]
    [InlineData("/tags/x")]
    public void ValueSchema_ReturnsNullWhereTheSchemaDeclaresNothing(string location)
    {
        Assert.Null(ContractShapes.ValueSchema(Ticket, location));
    }

    [Theory]
    [InlineData("""{"$ref":"#/$defs/missing","$defs":{}}""")]
    [InlineData("""{"$ref":"https://example.com/x"}""")]
    [InlineData("""{"$ref":"#/$defs/a","minLength":1,"$defs":{"a":{"type":"string"}}}""")]
    [InlineData("""{"$ref":"#","type":"object"}""")]
    [InlineData("""{"$ref":"#/$defs/a","$defs":{"a":{"$ref":"#/$defs/a"}}}""")]
    [InlineData("""{"$ref":"#/0"}""")]
    public void ValueSchema_RefusesAReferenceItCannotInline(string schema)
    {
        Assert.Null(ContractShapes.ValueSchema(Parse(schema), ""));
    }

    [Fact]
    public void ValueSchema_RefusesAnUninlinableChild()
    {
        var schema = Parse("""{"properties":{"x":{"$ref":"#/nope"}},"anyOf":[{"$ref":"#/nope"}],"items":{"$ref":"#/nope"}}""");

        Assert.Null(ContractShapes.ValueSchema(schema, ""));
        Assert.Null(ContractShapes.ValueSchema(Parse("""{"anyOf":[{"$ref":"#/nope"}]}"""), ""));
        Assert.Null(ContractShapes.ValueSchema(Parse("""{"items":{"$ref":"#/nope"}}"""), ""));
        Assert.Null(ContractShapes.ValueSchema(Parse("""{"properties":{"x":{"$ref":"#/nope"}}}"""), "/x"));
    }

    [Fact]
    public void ValueSchema_RefusesARecursiveReference_AndInlinesAnyOf()
    {
        var recursive = Parse("""{"properties":{"x":{"$ref":"#/$defs/a"}},"$defs":{"a":{"$ref":"#/$defs/a"}}}""");

        Assert.Null(ContractShapes.ValueSchema(recursive, "/x"));
        Assert.Equal("""{"anyOf":[{"type":"string"},{"type":"integer"}]}""", ContractShapes.ValueSchema(Parse("""{"anyOf":[{"type":"string"},{"$ref":"#/$defs/i"}],"$defs":{"i":{"type":"integer"}}}"""), "")!.Value.GetRawText());
    }

    [Fact]
    public void AnEmptyPropertyName_IsAPropertyNotAnIndex()
    {
        var schema = Parse("""{"type":"object","properties":{"":{"type":"string"}},"required":[""]}""");

        Assert.Equal("""{"type":"string"}""", ContractShapes.ValueSchema(schema, "/")!.Value.GetRawText());
        Assert.True(ContractShapes.AlwaysPresent(schema, "/"));
        Assert.Null(ContractShapes.ValueSchema(Ticket, "/"));
    }

    [Fact]
    public void ValueSchema_KeepsNonSchemaKeywordValues()
    {
        Assert.Equal("""{"enum":[1,2],"properties":true}""", ContractShapes.ValueSchema(Parse("""{"enum":[1,2],"properties":true}"""), "")!.Value.GetRawText());
        Assert.Equal("true", ContractShapes.ValueSchema(Parse("true"), "")!.Value.GetRawText());
    }

    [Theory]
    [InlineData("/id", true)]
    [InlineData("/owner/name", true)]
    [InlineData("/tags", false)]
    [InlineData("/tags/0", false)]
    [InlineData("/nope", false)]
    [InlineData("", true)]
    public void AlwaysPresent_FollowsRequiredAtEveryStep(string location, bool expected)
    {
        Assert.Equal(expected, ContractShapes.AlwaysPresent(Ticket, location));
    }

    [Fact]
    public void AlwaysPresent_IsFalseThroughAnUninlinableStep()
    {
        Assert.False(ContractShapes.AlwaysPresent(Parse("""{"$ref":"#/nope"}"""), "/x"));
        Assert.False(ContractShapes.AlwaysPresent(Parse("""{"type":"object","required":["x"]}"""), "/x"));
        Assert.False(ContractShapes.AlwaysPresent(Parse("""{"type":"object","properties":{"x":{}}}"""), "/x"));
    }

    [Fact]
    public void SameShape_IgnoresAnnotationsOnly()
    {
        Assert.True(ContractShapes.SameShape(Parse("""{"type":"string","description":"a"}"""), Parse("""{"description":"b","type":"string","examples":["x"]}""")));
        Assert.False(ContractShapes.SameShape(Parse("""{"type":"string"}"""), Parse("""{"type":"string","minLength":1}""")));
    }

    [Fact]
    public void RequiredProperties_ListsAnObjectsRequiredProperties()
    {
        Assert.Equal(["id", "owner"], ContractShapes.RequiredProperties(Ticket));
        Assert.Empty(ContractShapes.RequiredProperties(Parse("""{"type":"string"}""")));
        Assert.Empty(ContractShapes.RequiredProperties(Parse("true")));
    }

    [Fact]
    public void PublicMembers_RefuseANullLocation()
    {
        Assert.Throws<ArgumentNullException>(() => ContractShapes.ValueSchema(Ticket, null!));
        Assert.Throws<ArgumentNullException>(() => ContractShapes.AlwaysPresent(Ticket, null!));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
