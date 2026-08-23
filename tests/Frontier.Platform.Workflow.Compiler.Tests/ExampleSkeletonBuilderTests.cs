using System.Text.Json;
using System.Text.Json.Nodes;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S9.43 (doc 19 §A4-R2/C-31): <see cref="ExampleSkeletonBuilder"/> backs the A4 "Use example"
/// prefill — every generated example must be real, canonical-wire-form JSON a tester could
/// paste straight into the sandbox test-run input and have it pass the contract's own
/// <see cref="IVersionedContract.Validate"/>.
/// </summary>
public sealed class ExampleSkeletonBuilderTests
{
    [Fact]
    public void Build_EngagementBriefSection_RoundTripsAndValidates()
    {
        var node = ExampleSkeletonBuilder.Build(typeof(BriefArtifact));

        var section = Deserialize<BriefArtifact>(node);
        section.Validate(); // must not throw
        Assert.Equal("1.0", section.SchemaVersion);
        Assert.Equal("example", section.Narrative);
    }

    [Fact]
    public void Build_ApproachSection_DecimalIsCanonicalStringForm()
    {
        var node = ExampleSkeletonBuilder.Build(typeof(PlanArtifact));

        // FixedPrecisionDecimalConverter's wire form: a string, not a JSON number.
        Assert.Equal(JsonValueKind.String, node["cost_estimate"]!.GetValue<JsonElement>().ValueKind);

        var section = Deserialize<PlanArtifact>(node);
        section.Validate();
        Assert.Equal("example", section.Strategy);
        Assert.Equal(0m, section.CostEstimate);
    }

    [Fact]
    public void Build_PricingSection_ListGetsOneElement_DefaultedStringLeftAlone()
    {
        var node = ExampleSkeletonBuilder.Build(typeof(RateCardArtifact));

        var section = Deserialize<RateCardArtifact>(node);
        section.Validate();
        Assert.Single(section.UnitRates);
        Assert.Equal("example", section.UnitRates[0].Role);
        Assert.Equal(0m, section.UnitRates[0].Rate);
        // DiscountTerms defaults to string.Empty via its own property initializer — the
        // builder must leave an already-defaulted property alone, not overwrite it.
        Assert.Equal(string.Empty, section.DiscountTerms);
    }

    [Fact]
    public void Build_ScopeSection_StringListGetsOneElement()
    {
        var node = ExampleSkeletonBuilder.Build(typeof(SummaryArtifact));

        var section = Deserialize<SummaryArtifact>(node);
        section.Validate();
        Assert.Equal("example", section.Title);
        Assert.Single(section.Objectives);
        Assert.Equal("example", section.Objectives[0]);
    }

    [Fact]
    public void Build_TypeWithDateTimeSmartEnumAndDictionary_ProducesRealisticPlaceholders()
    {
        var node = ExampleSkeletonBuilder.Build(typeof(RichFixtureContract));

        var fixture = Deserialize<RichFixtureContract>(node);
        Assert.NotEqual(default, fixture.OccurredAtUtc);
        Assert.Equal(DecisionKind.Approve, fixture.Decision);
        Assert.Single(fixture.Counts);
        Assert.Equal(0, fixture.Counts["key"]);
    }

    [Fact]
    public void IsSmartEnum_RealSmartEnumAndPlainClass_DistinguishesCorrectly()
    {
        Assert.True(ExampleSkeletonBuilder.IsSmartEnum(typeof(DecisionKind)));
        Assert.False(ExampleSkeletonBuilder.IsSmartEnum(typeof(string)));
    }

    [Fact]
    public void FirstSmartEnumValue_DecisionKind_ReturnsFirstDeclaredValue() =>
        Assert.Equal(DecisionKind.Approve, ExampleSkeletonBuilder.FirstSmartEnumValue(typeof(DecisionKind)));

    [Fact]
    public void TryGetDictionaryValueType_NonDictionaryType_ReturnsFalse()
    {
        var resolved = ExampleSkeletonBuilder.TryGetDictionaryValueType(typeof(string), out var valueType);

        Assert.False(resolved);
        Assert.Equal(typeof(object), valueType);
    }

    [Fact]
    public void TryGetEnumerableElementType_ArrayType_ReturnsElementType()
    {
        var resolved = ExampleSkeletonBuilder.TryGetEnumerableElementType(typeof(int[]), out var elementType);

        Assert.True(resolved);
        Assert.Equal(typeof(int), elementType);
    }

    [Fact]
    public void TryGetEnumerableElementType_NonEnumerableType_ReturnsFalse()
    {
        var resolved = ExampleSkeletonBuilder.TryGetEnumerableElementType(typeof(int), out var elementType);

        Assert.False(resolved);
        Assert.Equal(typeof(object), elementType);
    }

    [Fact]
    public void CreateInstance_TypeWithNoParameterlessConstructor_ReturnsNull() =>
        Assert.Null(ExampleSkeletonBuilder.CreateInstance(typeof(NoParameterlessConstructor), depth: 0));

    [Fact]
    public void CreateInstance_BeyondMaxDepth_ReturnsNull() =>
        Assert.Null(ExampleSkeletonBuilder.CreateInstance(typeof(BriefArtifact), depth: 100));

    private static T Deserialize<T>(JsonNode node) =>
        JsonSerializer.Deserialize<T>(node, CanonicalProfile.Options)!;

    private sealed class NoParameterlessConstructor
    {
        internal NoParameterlessConstructor(int value) => Value = value;
        internal int Value { get; }
    }

    /// <summary>Test-only fixture exercising the DateTime/smart-enum/dictionary branches no shipped entry contract currently combines.</summary>
    private sealed record RichFixtureContract : IVersionedContract
    {
        public string SchemaVersion { get; init; } = "1.0";
        public required DateTime OccurredAtUtc { get; init; }
        public required DecisionKind Decision { get; init; }
        public required IReadOnlyDictionary<string, int> Counts { get; init; }
        public void Validate() { }
    }
}
