namespace Frontier.Platform.Contracts.Tests;

public sealed class SchemaWalkTests
{
    [Fact]
    public void All_VisitsEveryApplicator_InDocumentOrder_WithPositions()
    {
        var schema = Json.Parse("""
            {
              "properties": { "a/b": { "items": { "type": "string" } }, "description": true },
              "allOf": [ { "not": {} } ],
              "$defs": { "x~y": {} },
              "prefixItems": [ {} ],
              "enum": [ { "properties": { "ignored": {} } } ],
              "examples": [ { "properties": {} } ]
            }
            """);

        var visited = SchemaWalk.All(schema).Select(s => (s.Pointer, s.Position)).ToList();

        Assert.Equal(
            [
                ("", SubschemaPosition.Root),
                ("/properties/a~1b", SubschemaPosition.Property),
                ("/properties/a~1b/items", SubschemaPosition.Other),
                ("/properties/description", SubschemaPosition.Property),
                ("/allOf/0", SubschemaPosition.Composition),
                ("/allOf/0/not", SubschemaPosition.Other),
                ("/$defs/x~0y", SubschemaPosition.Other),
                ("/prefixItems/0", SubschemaPosition.Other),
            ],
            visited);
    }

    [Fact]
    public void All_IgnoresApplicatorsOfTheWrongShape()
    {
        var schema = Json.Parse("""{ "properties": [], "allOf": {}, "if": true }""");

        Assert.Equal(["", "/if"], SchemaWalk.All(schema).Select(s => s.Pointer));
    }

    [Fact]
    public void Children_OfABooleanSchema_IsEmpty() =>
        Assert.Empty(SchemaWalk.Children(new Subschema("", Json.Parse("true"), SubschemaPosition.Root)));
}
