using Celly.Types;
using Celly.Values;

namespace Frontier.Platform.Contracts.Tests;

/// <summary>The schema-to-CEL type mapping (ADR-PA38; Mark, 2026-10-29: self is schema-typed, numbers are doubles).</summary>
public sealed class CelSchemaTypesTests
{
    [Theory]
    [InlineData("""{ "type": "string" }""", "string")]
    [InlineData("""{ "type": "integer" }""", "double")]
    [InlineData("""{ "type": "number" }""", "double")]
    [InlineData("""{ "type": "boolean" }""", "bool")]
    [InlineData("""{ "type": "array", "items": { "type": "string" } }""", "list(string)")]
    [InlineData("""{ "type": "array" }""", "list(dyn)")]
    [InlineData("""{ "type": "object" }""", "map(string, dyn)")]
    [InlineData("""{ "type": "object", "additionalProperties": { "type": "number" } }""", "map(string, double)")]
    [InlineData("""{ "type": "object", "additionalProperties": true }""", "map(string, dyn)")]
    [InlineData("""{ "type": ["string", "null"] }""", "dyn")]
    [InlineData("""{ "oneOf": [ { "type": "string" } ] }""", "dyn")]
    [InlineData("""{ "type": "null" }""", "dyn")]
    [InlineData("""true""", "dyn")]
    public void SelfType_FollowsTheSchema(string schema, string expected) =>
        Assert.Equal(expected, new CelSchemaTypes(Json.Parse(schema)).SelfType.ToString());

    [Theory]
    [InlineData("""{ "type": "object", "properties": {} }""")]
    [InlineData("""{ "properties": {} }""")]
    public void AnObjectWithProperties_IsAStruct(string schema)
    {
        var types = new CelSchemaTypes(Json.Parse(schema));

        Assert.Equal(CelTypeKind.Struct, types.SelfType.Kind);
        Assert.Equal(CelSchemaTypes.StructPrefix + "0", types.SelfType.Name);
        Assert.Equal(string.Empty, types.StructPointer(types.SelfType.Name));
    }

    [Fact]
    public void LocalRefs_AreFollowed_AndRecursionTerminates()
    {
        var types = new CelSchemaTypes(Json.Parse("""
            {
              "type": "object",
              "properties": { "child": { "$ref": "#" }, "list": { "$ref": "#/$defs/list" }, "anchored": { "$ref": "#node" } },
              "$defs": { "list": { "type": "array", "items": { "$ref": "#/$defs/list" } }, "a": { "$ref": "#/$defs/b" }, "b": { "$ref": "#/$defs/a" } }
            }
            """));
        var self = types.SelfType.Name;

        Assert.Equal(types.SelfType.ToString(), types.FindStructFieldType(self, "child")!.ToString());
        Assert.Equal("list(dyn)", types.FindStructFieldType(self, "list")!.ToString());
        Assert.Equal("dyn", types.FindStructFieldType(self, "anchored")!.ToString());
        Assert.Equal("dyn", types.TypeAt("/$defs/a").ToString());
        Assert.Null(types.FindStructFieldType(self, "missing"));
        Assert.Null(types.FindStructFieldType("contract.S99", "child"));
    }

    [Fact]
    public void FieldPointer_IsADeclaredProperty_OrAMapsValues_OrNothing()
    {
        var types = new CelSchemaTypes(Json.Parse("""
            { "properties": { "a/b": { "type": "string" }, "m": { "type": "object", "additionalProperties": { "type": "string" } }, "open": { "type": "object", "additionalProperties": true }, "s": { "type": "string" } } }
            """));

        Assert.Equal("/properties/a~1b", types.FieldPointer("", "a/b"));
        Assert.Null(types.FieldPointer("", "nope"));
        Assert.Equal("/properties/m/additionalProperties", types.FieldPointer("/properties/m", "anything"));
        Assert.Null(types.FieldPointer("/properties/open", "anything"));
        Assert.Null(types.FieldPointer("/properties/s", "anything"));
    }

    [Fact]
    public void ElementPointer_IsAnArraysItems_OrAMapsKeys()
    {
        var types = new CelSchemaTypes(Json.Parse("""
            { "properties": { "l": { "type": "array", "items": { "type": "string" } }, "bare": { "type": "array" }, "m": { "type": "object", "propertyNames": { "maxLength": 3 } }, "s": { "type": "string" } } }
            """));

        Assert.Equal("/properties/l/items", types.ElementPointer("/properties/l"));
        Assert.Null(types.ElementPointer("/properties/bare"));
        Assert.Equal("/properties/m/propertyNames", types.ElementPointer("/properties/m"));
        Assert.Null(types.ElementPointer("/properties/s"));
    }

    [Theory]
    [InlineData("/properties/l", 5UL)]
    [InlineData("/properties/s", 7UL)]
    [InlineData("/properties/m", 2UL)]
    [InlineData("", 9UL)]
    [InlineData("/properties/unbounded", null)]
    [InlineData("/properties/n", null)]
    [InlineData("/properties/bad", null)]
    public void MaxSize_ReadsTheLimitForTheValuesKind(string location, ulong? expected)
    {
        var types = new CelSchemaTypes(Json.Parse("""
            { "maxProperties": 9, "properties": {
                "l": { "type": "array", "maxItems": 5 }, "s": { "type": "string", "maxLength": 7 }, "m": { "type": "object", "maxProperties": 2 },
                "unbounded": { "type": "string" }, "n": { "type": "number", "maxItems": 3 }, "bad": { "type": "string", "maxLength": -1 } } }
            """));

        Assert.Equal(expected, types.MaxSize(location));
    }

    [Theory]
    [InlineData("/allOf/1", true)]
    [InlineData("/allOf/2", false)]
    [InlineData("/allOf/x", false)]
    [InlineData("/allOf/0/type/deeper", false)]
    [InlineData("/missing", false)]
    public void Resolve_WalksObjectsAndArrays(string location, bool found) =>
        Assert.Equal(found, new CelSchemaTypes(Json.Parse("""{ "allOf": [ { "type": "string" }, {} ] }""")).Resolve(location).HasValue);

    [Fact]
    public void TypeAt_APointerToNothing_IsDyn() =>
        Assert.Equal("dyn", new CelSchemaTypes(Json.Parse("""{ "type": "string" }""")).TypeAt("/missing").ToString());

    [Fact]
    public void TheProviderAndAdapter_OnlyKnowThisSchemasTypes()
    {
        var types = new CelSchemaTypes(Json.Parse("""{ "properties": {} }"""));

        Assert.NotNull(types.FindStructType(types.SelfType.Name));
        Assert.Null(types.FindStructType("google.protobuf.Struct"));
        Assert.Null(types.FindIdent("x"));
        Assert.True(types.NewValue(types.SelfType.Name, []).IsError);
        Assert.Same(BoolValue.True, types.NativeToValue(BoolValue.True));
        Assert.True(types.NativeToValue("raw").IsError);
        Assert.True(types.NativeToValue(null).IsError);
    }
}
