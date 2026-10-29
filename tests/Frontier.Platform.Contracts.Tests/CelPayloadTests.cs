using Celly.Values;

namespace Frontier.Platform.Contracts.Tests;

public sealed class CelPayloadTests
{
    private static readonly CelSchemaTypes Types = new(Json.Parse("""
        {
          "type": "object",
          "properties": {
            "n": { "type": "integer" },
            "items": { "type": "array", "items": { "type": "object", "properties": { "x": { "type": "number" } } } },
            "m": { "type": "object", "additionalProperties": { "type": "string" } },
            "any": {}
          }
        }
        """));

    [Fact]
    public void Convert_TypesTheObjectAsAStruct_WithItsDeclaredFieldsOnly()
    {
        var value = Assert.IsType<CelStructValue>(CelPayload.Convert(Types, Json.Parse("""{ "n": 3, "undeclared": true }"""), ""));

        Assert.Equal(Types.SelfType.Name, value.Type.Name);
        Assert.Equal(["n"], value.Fields.Keys);
        Assert.True(DoubleValue.Of(3).EqualTo(value.GetField("n")));
        Assert.True(value.GetField("undeclared").IsError);
        Assert.True(BoolValue.True.EqualTo(value.HasField("n")));
        Assert.True(BoolValue.False.EqualTo(value.HasField("items")));
    }

    [Fact]
    public void Convert_TypesArrayElementsAndMapValues_AndOrdersMapKeys()
    {
        var value = (CelStructValue)CelPayload.Convert(Types, Json.Parse("""{ "items": [ { "x": 1.5 } ], "m": { "b": "2", "a": "1" } }"""), "");

        var item = Assert.IsType<CelStructValue>(Assert.IsType<ListValue>(value.GetField("items")).Elements[0]);
        Assert.True(DoubleValue.Of(1.5).EqualTo(item.GetField("x")));
        var map = Assert.IsType<MapValue>(value.GetField("m"));
        Assert.Equal(["a", "b"], map.Keys.Select(k => k.ToNative()));
    }

    [Fact]
    public void Convert_AnUndescribedValue_IsPlainCel()
    {
        var value = (CelStructValue)CelPayload.Convert(Types, Json.Parse("""{ "any": { "z": [true, false, null, "s"], "y": 1 } }"""), "");

        var map = Assert.IsType<MapValue>(value.GetField("any"));
        Assert.Equal(["y", "z"], map.Keys.Select(k => k.ToNative()));
        Assert.Equal(
            [true, false, null, "s"],
            Assert.IsType<ListValue>(map.Get(StringValue.Of("z"))).Elements.Select(e => e.ToNative()));
        Assert.IsType<MapValue>(CelPayload.Convert(Types, Json.Parse("""{ "a": 1 }"""), null));
        Assert.IsType<ListValue>(CelPayload.Convert(Types, Json.Parse("[1]"), null));
    }

    [Fact]
    public void Structs_AreEqualByTypeAndFields()
    {
        var a = CelPayload.Convert(Types, Json.Parse("""{ "n": 1 }"""), "");

        Assert.True(a.EqualTo(CelPayload.Convert(Types, Json.Parse("""{ "n": 1 }"""), "")));
        Assert.False(a.EqualTo(CelPayload.Convert(Types, Json.Parse("""{ "n": 2 }"""), "")));
        Assert.False(a.EqualTo(CelPayload.Convert(Types, Json.Parse("""{ "n": 1, "any": 1 }"""), "")));
        Assert.False(a.EqualTo(CelPayload.Convert(Types, Json.Parse("""{ "any": 1 }"""), "")));
        Assert.False(a.EqualTo(CelPayload.Convert(Types, Json.Parse("""{ "x": 1 }"""), "/properties/items/items")));
        Assert.False(a.EqualTo(DoubleValue.Of(1)));
        Assert.Equal(new Dictionary<string, object> { ["n"] = 1.0 }, a.ToNative());
    }
}
