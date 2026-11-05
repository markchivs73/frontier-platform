using System.Text.Json.Serialization;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// S13.185: <see cref="ActivityShapeRules"/> driven with a stand-in catalogue, so the update, bump
/// and two-generation paths are proven without rewriting the committed file. The stand-in's
/// versioned implementation plays SDK v1.25.0's <c>[DurableTask(Version = ...)]</c> registration.
/// </summary>
public sealed class ActivityShapeRulesTests
{
    private static readonly ActivityImplementation Unversioned = new("SampleActivity", ActivityImplementation.Platform, typeof(SampleV1), typeof(SampleV1), null);

    private static readonly ActivityImplementation Renamed = Unversioned with { Input = typeof(SampleV2) };

    private static readonly ActivityImplementation Additive = Unversioned with { Input = typeof(SampleV1Extended) };

    private static readonly ActivityShapeSnapshot GenerationOne = Snapshot(ActivityShapeRules.Generation(1, Code(Unversioned)));

    [Fact]
    public void Rewrite_WithNoMode_IsNull() =>
        Assert.Null(ActivityShapeRules.Rewrite(GenerationOne, mode: null, Code(Additive)));

    [Fact]
    public void Rewrite_Update_AcceptsAnAdditiveChangeIntoTheCurrentGeneration()
    {
        var updated = ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Update, Code(Additive))!;

        var generation = Assert.Single(updated.Generations);
        Assert.Equal(1, generation.Generation);
        Assert.Empty(ActivityShapeRules.Drift(updated, Code(Additive)));
    }

    [Fact]
    public void Rewrite_Update_RefusesABreakingChange()
    {
        Assert.Contains(ActivityShapeRules.Drift(GenerationOne, Code(Renamed)), change => change.Breaking);
        Assert.Null(ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Update, Code(Renamed)));
    }

    [Fact]
    public void Rewrite_Bump_AppendsTheNextGenerationAsTheCodeProducesIt()
    {
        var bumped = ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Bump, Code(Renamed))!;

        Assert.Equal([1, 2], bumped.Generations.Select(g => g.Generation));
        Assert.Same(GenerationOne.Generations[0], bumped.Generations[0]);
        Assert.Empty(ActivityShapeRules.Drift(bumped, Code(Renamed)));
    }

    [Fact]
    public void Bump_WithTheOldImplementationUnregistered_FailsTheRegistrationRule()
    {
        var bumped = ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Bump, Code(Renamed))!;

        var missing = Assert.Single(ActivityShapeRules.Unregistered(bumped, Code(Renamed)));
        Assert.Equal("generation 1: BREAKING SampleActivity: activity removed or renamed", missing);
    }

    [Fact]
    public void Bump_WithTheOldImplementationKeptUnderItsVersion_PassesEveryRule()
    {
        var code = Code(Unversioned with { Generation = 1 }, Renamed);
        var bumped = ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Bump, code)!;

        Assert.Empty(ActivityShapeRules.Drift(bumped, code));
        Assert.Empty(ActivityShapeRules.Unregistered(bumped, code));
        Assert.Empty(ActivityShapeRules.IncompatibleEdits(bumped, GenerationOne));
    }

    [Fact]
    public void Unregistered_AnOldImplementationWhoseShapeDrifted_IsReported()
    {
        var bumped = ActivityShapeRules.Rewrite(GenerationOne, ActivityShapeRules.Bump, Code(Renamed))!;

        var missing = ActivityShapeRules.Unregistered(bumped, Code(Renamed with { Generation = 1 }, Renamed));

        Assert.Contains("generation 1: BREAKING SampleActivity: input $.a removed or renamed", missing);
    }

    [Fact]
    public void IncompatibleEdits_ABreakingHandEditWithinAGeneration_IsReported()
    {
        var edited = Snapshot(ActivityShapeRules.Generation(1, Code(Renamed)));

        Assert.Contains("generation 1: BREAKING SampleActivity: input $.a removed or renamed", ActivityShapeRules.IncompatibleEdits(edited, GenerationOne));
    }

    [Fact]
    public void IncompatibleEdits_DroppingTheBaseBranchsCurrentGeneration_IsReported()
    {
        var replaced = Snapshot(ActivityShapeRules.Generation(2, Code(Renamed)));

        Assert.Equal(["dropped generation 1, the base branch's current one"], ActivityShapeRules.IncompatibleEdits(replaced, GenerationOne));
    }

    [Fact]
    public void IncompatibleEdits_AnAdditiveEdit_IsAccepted() =>
        Assert.Empty(ActivityShapeRules.IncompatibleEdits(Snapshot(ActivityShapeRules.Generation(1, Code(Additive))), GenerationOne));

    private static ShapeSource Code(params ActivityImplementation[] platform) =>
        (generation, current) => [.. ActivityCatalogue.ShapesOf(platform, generation, current).Where(a => a.ImplementedBy == ActivityImplementation.Platform)];

    private static ActivityShapeSnapshot Snapshot(GenerationShapes generation) => new() { Generations = [generation] };

    /// <summary>The generation-1 input.</summary>
    internal sealed record SampleV1
    {
        /// <summary>A required member.</summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("a")]
        public required string A { get; init; }
    }

    /// <summary><see cref="SampleV1"/> with an optional member added.</summary>
    internal sealed record SampleV1Extended
    {
        /// <summary>The same required member.</summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("a")]
        public required string A { get; init; }

        /// <summary>The optional addition.</summary>
        [JsonPropertyOrder(1)]
        [JsonPropertyName("note")]
        public string? Note { get; init; }
    }

    /// <summary><see cref="SampleV1"/> with its member renamed: a breaking change.</summary>
    internal sealed record SampleV2
    {
        /// <summary>The renamed member.</summary>
        [JsonPropertyOrder(0)]
        [JsonPropertyName("b")]
        public required string B { get; init; }
    }
}
