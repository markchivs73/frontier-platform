using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler.Schema;

namespace Frontier.Platform.Workflow.Compiler.Tests.Schema;

/// <summary>Unit tests for <see cref="SchemaTypeMapper"/> — CLR type → wire token mapping (doc 14 §7).</summary>
public sealed class SchemaTypeMapperTests
{
    [Theory]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(int?), "integer")]
    [InlineData(typeof(bool?), "boolean")]
    [InlineData(typeof(IReadOnlyList<string>), "array<string>")]
    [InlineData(typeof(List<string>), "array<string>")]
    public void MapToken_PrimitivesAndLists_MapToExpectedTokens(Type type, string expected) =>
        Assert.Equal(expected, SchemaTypeMapper.MapToken(type));

    [Fact]
    public void MapToken_SmartEnum_MapsToEnumToken() =>
        Assert.Equal("enum:GateKind", SchemaTypeMapper.MapToken(typeof(GateKind)));

    [Fact]
    public void MapToken_ComplexContract_MapsToObjectToken() =>
        Assert.Equal("object:ContextRequest", SchemaTypeMapper.MapToken(typeof(ContextRequest)));

    [Fact]
    public void IsStringList_DistinguishesStringListsFromStringsAndOtherLists()
    {
        Assert.True(SchemaTypeMapper.IsStringList(typeof(IReadOnlyList<string>)));
        Assert.False(SchemaTypeMapper.IsStringList(typeof(string)));
        Assert.False(SchemaTypeMapper.IsStringList(typeof(IReadOnlyList<int>)));
    }

    [Fact]
    public void IsSmartEnum_TrueOnlyForSmartEnumDerivedTypes()
    {
        Assert.True(SchemaTypeMapper.IsSmartEnum(typeof(EdgeKind)));
        Assert.False(SchemaTypeMapper.IsSmartEnum(typeof(int)));
        Assert.False(SchemaTypeMapper.IsSmartEnum(typeof(string)));
    }

    [Fact]
    public void TryGetComplexType_TrueForContracts_FalseForPrimitivesEnumsAndCollections()
    {
        Assert.True(SchemaTypeMapper.TryGetComplexType(typeof(ContextRequest), out var t));
        Assert.Equal(typeof(ContextRequest), t);

        Assert.False(SchemaTypeMapper.TryGetComplexType(typeof(string), out _));
        Assert.False(SchemaTypeMapper.TryGetComplexType(typeof(int), out _));
        Assert.False(SchemaTypeMapper.TryGetComplexType(typeof(GateKind), out _));
        Assert.False(SchemaTypeMapper.TryGetComplexType(typeof(IReadOnlyList<string>), out _));
    }
}
