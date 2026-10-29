using Celly;
using Celly.Checking;
using Celly.Types;

namespace Frontier.Platform.Contracts.Tests;

public sealed class RuleCostHintsTests
{
    private static readonly CelSchemaTypes Types = new(Json.Parse("""
        { "properties": {
            "a": { "type": "array", "maxItems": 3, "items": { "properties": { "s": { "type": "string", "maxLength": 4 } } } },
            "b": { "type": "array", "maxItems": 2, "items": { "type": "number" } } } }
        """));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("self.a", "/properties/a")]
    [InlineData("i.s", "/properties/a/items/properties/s")]
    [InlineData("self.nope.deeper", null)]
    [InlineData("other", null)]
    public void PointerOfPath_ResolvesSelfAndLoopVariables(string? path, string? expected) =>
        Assert.Equal(expected, Hints("self.a.all(i, i.s == 'x')").PointerOfPath(path));

    [Fact]
    public void ALoopVariableBoundToTwoRanges_IsLeftUnmapped() =>
        Assert.Null(Hints("self.a.all(i, true) && self.b.all(i, true)").PointerOfPath("i"));

    [Fact]
    public void ALoopVariableReboundToTheSameRange_StaysMapped() =>
        Assert.Equal("/properties/a/items", Hints("self.a.all(i, true) && self.a.exists(i, true)").PointerOfPath("i"));

    [Fact]
    public void ARangeThatIsNotASelectChain_LeavesItsVariableUnmapped() =>
        Assert.Null(Hints("[1, 2].all(i, true)").PointerOfPath("i"));

    [Theory]
    [InlineData("self.nope.deeper")]
    [InlineData("other.a")]
    public void ASelectOnSomethingThatIsNotAPath_IsNotAPath(string expression) =>
        Assert.Null(Hints("true").PointerOf(Parse(expression).Expr));

    [Fact]
    public void AHasTest_IsNotAPath() =>
        Assert.Null(Hints("true").PointerOf(Parse("has(self.a)").Expr));

    [Theory]
    [InlineData("self.a.map(i, i.s)", 3UL)]
    [InlineData("self.a.map(i, i.s).size() > 0 && self.b.filter(x, x > 1.0).size() > 0", 3UL)]
    [InlineData("[1].map(x, x).size() > 0", null)]
    [InlineData("self.a.map(i, i.s).size() > 0 && [1].map(x, x).size() > 0", null)]
    [InlineData("[1].map(x, x).size() > 0 && self.a.map(i, i.s).size() > 0", null)]
    public void TheListAMacroBuilds_IsNoLongerThanItsRange(string expression, ulong? expected) =>
        Assert.Equal(expected, Hints(expression).EstimateSize("@result", CelType.ListDyn)?.Max);

    [Fact]
    public void EstimateSize_ReturnsTheSchemasLimit_OrNothing()
    {
        var hints = Hints("self.a.all(i, i.s == 'x')");

        Assert.Equal(new SizeEstimate(0, 3), hints.EstimateSize("self.a", CelType.ListDyn));
        Assert.Null(hints.EstimateSize("self.b.nope", CelType.Dyn));
    }

    private static RuleCostHints Hints(string expression) => new(Types, Parse(expression).Expr);

    private static Celly.Ast.CelAbstractSyntax Parse(string expression) =>
        CelEnv.Create(new CelEnvSettings { TypeProvider = Types, Adapter = Types, Declarations = [new VariableDecl("self", Types.SelfType)] }).Parse(expression).Ast!;
}
