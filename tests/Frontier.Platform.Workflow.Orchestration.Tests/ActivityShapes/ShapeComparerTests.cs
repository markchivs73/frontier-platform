namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>S13.185: the rule <see cref="ShapeComparer"/> applies to decide what needs a generation bump.</summary>
public sealed class ShapeComparerTests
{
    private static readonly Dictionary<string, string> Base = new()
    {
        ["$"] = "object",
        ["$.id"] = "string (required)",
        ["$.note"] = "string",
        ["$.parent"] = "object",
        ["$.kind"] = "enum(a|b)",
    };

    [Fact]
    public void Compare_IdenticalShapes_FindsNothing() =>
        Assert.Empty(ShapeComparer.Compare([Shape(Base)], [Shape(Base)]));

    [Fact]
    public void Compare_RemovedActivity_IsBreaking() =>
        AssertSingle(ShapeComparer.Compare([Shape(Base)], []), "activity removed or renamed", breaking: true);

    [Fact]
    public void Compare_AddedActivity_IsAdditive() =>
        AssertSingle(ShapeComparer.Compare([], [Shape(Base)]), "activity added", breaking: false);

    [Fact]
    public void Compare_RemovedMember_IsBreaking() =>
        AssertSingle(Diff(Without("$.note")), "input $.note removed or renamed", breaking: true);

    [Fact]
    public void Compare_ReshapedMember_IsBreaking() =>
        AssertSingle(Diff(With("$.note", "integer")), "input $.note changed from string to integer", breaking: true);

    [Fact]
    public void Compare_OptionalMemberBecomingRequired_IsBreaking() =>
        AssertSingle(Diff(With("$.note", "string (required)")), "input $.note became required", breaking: true);

    [Fact]
    public void Compare_RequiredMemberBecomingOptional_IsAdditive() =>
        AssertSingle(Diff(With("$.id", "string")), "input $.id is no longer required", breaking: false);

    [Fact]
    public void Compare_NewOptionalMember_IsAdditive() =>
        AssertSingle(Diff(With("$.extra", "string")), "input $.extra added as string", breaking: false);

    [Fact]
    public void Compare_NewRequiredMemberUnderAnExistingParent_IsBreaking() =>
        AssertSingle(Diff(With("$.parent.child", "string (required)")), "input $.parent.child added as string (required)", breaking: true);

    [Fact]
    public void Compare_NewRequiredMemberUnderANewParent_IsAdditive()
    {
        var changes = Diff(With("$.fresh", "object").Concat(With("$.fresh.child", "string (required)")).DistinctBy(p => p.Key).ToDictionary());

        Assert.All(changes, change => Assert.False(change.Breaking));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Compare_ANewRequiredRoot_IsAdditive() =>
        AssertSingle(
            ShapeComparer.Compare([Shape(new Dictionary<string, string>())], [Shape(new Dictionary<string, string> { ["$"] = "object (required)" })]),
            "input $ added as object (required)",
            breaking: false);

    [Fact]
    public void Compare_AnEnumLosingAValue_IsBreaking() =>
        AssertSingle(Diff(With("$.kind", "enum(a|c)")), "input $.kind lost enum values b", breaking: true);

    [Fact]
    public void Compare_AnEnumOnlyGainingValues_IsAdditive() =>
        AssertSingle(Diff(With("$.kind", "enum(a|b|c)")), "input $.kind gained enum values c", breaking: false);

    [Fact]
    public void Compare_AnEnumGainingValuesAndBecomingRequired_IsABreakingReshape() =>
        AssertSingle(Diff(With("$.kind", "enum(a|b|c) (required)")), "input $.kind changed from enum(a|b) to enum(a|b|c) (required)", breaking: true);

    [Fact]
    public void Compare_AnEnumBecomingAString_IsABreakingReshape() =>
        AssertSingle(Diff(With("$.kind", "string")), "input $.kind changed from enum(a|b) to string", breaking: true);

    [Fact]
    public void Compare_ADecimalScaleChange_IsABreakingReshape() =>
        AssertSingle(
            ShapeComparer.Compare([Shape(new Dictionary<string, string> { ["$"] = "decimal(scale=4)" })], [Shape(new Dictionary<string, string> { ["$"] = "decimal(scale=2)" })]),
            "input $ changed from decimal(scale=4) to decimal(scale=2)",
            breaking: true);

    [Theory]
    [InlineData("string", "enum(a)")]
    [InlineData("enum(a)", "string")]
    public void EnumChange_UnlessBothAreEnums_IsNull(string before, string after) =>
        Assert.Null(ShapeComparer.EnumChange(before, after));

    [Fact]
    public void Compare_OutputChanges_AreReportedOnTheOutputSide()
    {
        var changes = ShapeComparer.Compare([Shape(Base)], [Shape(Base) with { Output = Without("$.note") }]);

        AssertSingle(changes, "output $.note removed or renamed", breaking: true);
    }

    [Fact]
    public void Compare_TypeNameOrImplementerChange_IsAdditive()
    {
        Assert.False(Single(ShapeComparer.Compare([Shape(Base)], [Shape(Base) with { InputType = "Renamed" }])).Breaking);
        Assert.False(Single(ShapeComparer.Compare([Shape(Base)], [Shape(Base) with { OutputType = "Renamed" }])).Breaking);
        Assert.False(Single(ShapeComparer.Compare([Shape(Base)], [Shape(Base) with { ImplementedBy = ActivityImplementation.Consumer }])).Breaking);
    }

    [Theory]
    [InlineData("$", null)]
    [InlineData("$.a", "$")]
    [InlineData("$.a.b", "$.a")]
    [InlineData("$.a[]", "$.a")]
    [InlineData("$.a{}", "$.a")]
    [InlineData("$.a<x>", "$.a")]
    [InlineData("$.a<x>.b", "$.a<x>")]
    public void Parent_IsTheNearestEnclosingPath(string path, string? parent)
    {
        ArgumentNullException.ThrowIfNull(path);
        Assert.Equal(parent, ShapeComparer.Parent(path));
    }

    [Fact]
    public void ShapeChange_ToString_LeadsWithItsSeverity()
    {
        Assert.Equal("BREAKING A: x", new ShapeChange("A", "x", Breaking: true).ToString());
        Assert.Equal("additive A: x", new ShapeChange("A", "x", Breaking: false).ToString());
    }

    private static IReadOnlyList<ShapeChange> Diff(Dictionary<string, string> input) =>
        ShapeComparer.Compare([Shape(Base)], [Shape(input)]);

    private static Dictionary<string, string> With(string path, string token) => new(Base) { [path] = token };

    private static Dictionary<string, string> Without(string path)
    {
        var members = new Dictionary<string, string>(Base);
        members.Remove(path);
        return members;
    }

    private static ActivityShape Shape(IReadOnlyDictionary<string, string> input) => new()
    {
        Name = "SampleActivity",
        ImplementedBy = ActivityImplementation.Platform,
        InputType = "SampleInput",
        OutputType = "SampleOutput",
        Input = input,
        Output = Base,
    };

    private static ShapeChange Single(IReadOnlyList<ShapeChange> changes) => Assert.Single(changes);

    private static void AssertSingle(IReadOnlyList<ShapeChange> changes, string description, bool breaking)
    {
        var change = Assert.Single(changes);
        Assert.Equal(description, change.Description);
        Assert.Equal(breaking, change.Breaking);
    }
}
