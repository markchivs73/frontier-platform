using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>S13.185: <see cref="ActivityCatalogue"/> finds the activities and the generation each serves.</summary>
public sealed class ActivityCatalogueTests
{
    [Fact]
    public void Describe_AnActivity_ReadsItsNameAndWireTypes()
    {
        var activity = ActivityCatalogue.Describe(typeof(PinMappingsActivity));

        Assert.Equal(new ActivityImplementation("PinMappingsActivity", ActivityImplementation.Platform, typeof(PinMappingsRequest), typeof(IReadOnlyList<ModelRoleConfig.ModelRolePin>), null), activity);
    }

    [Fact]
    public void Describe_AnUndecoratedActivity_FallsBackToItsTypeName() =>
        Assert.Equal(nameof(UndecoratedActivity), ActivityCatalogue.Describe(typeof(UndecoratedActivity))!.Name);

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(AbstractActivity))]
    public void Describe_ANonActivityOrAbstractType_IsNull(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Assert.Null(ActivityCatalogue.Describe(type));
    }

    [Fact]
    public void GenerationOf_ReadsAVersionPropertyWhenThereIsOne()
    {
        Assert.Equal(2, ActivityCatalogue.GenerationOf(new VersionedAttribute { Version = "2" }));
        Assert.Null(ActivityCatalogue.GenerationOf(new VersionedAttribute { Version = "" }));
        Assert.Null(ActivityCatalogue.GenerationOf(new DurableTaskAttribute("x")));
    }

    [Fact]
    public void Serves_AnUnversionedImplementation_OnlyTheCurrentGeneration()
    {
        var unversioned = new ActivityImplementation("A", ActivityImplementation.Platform, typeof(string), typeof(string), null);
        var versioned = unversioned with { Generation = 1 };

        Assert.True(unversioned.Serves(2, current: 2));
        Assert.False(unversioned.Serves(1, current: 2));
        Assert.True(versioned.Serves(1, current: 2));
        Assert.False(versioned.Serves(2, current: 2));
    }

    [Fact]
    public void Shapes_ForAnOlderGeneration_HoldNoUnversionedOrConsumerActivity() =>
        Assert.Empty(ActivityCatalogue.Shapes(generation: 1, current: 2));

    [Fact]
    public void Shapes_ForTheCurrentGeneration_HoldEveryActivity() =>
        Assert.Equal(ActivityCatalogue.PlatformImplemented().Count + ActivityCatalogue.ConsumerImplemented.Count, ActivityCatalogue.Shapes(1, 1).Count);

    [Theory]
    [InlineData(typeof(string), "String")]
    [InlineData(typeof(IReadOnlyDictionary<string, int>), "IReadOnlyDictionary<String, Int32>")]
    public void TypeName_ReadsGenericArguments(Type type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        Assert.Equal(name, ActivityImplementation.TypeName(type));
    }

    /// <summary>Stands in for SDK v1.25.0's <c>DurableTaskAttribute</c>, which adds <c>Version</c>.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class VersionedAttribute : Attribute
    {
        /// <summary>The registered version.</summary>
        public string? Version { get; init; }
    }

    /// <summary>An activity with no <see cref="DurableTaskAttribute"/>.</summary>
    internal sealed class UndecoratedActivity : TaskActivity<string, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, string input) => Task.FromResult(input);
    }

    /// <summary>An abstract activity, which is never registered.</summary>
    internal abstract class AbstractActivity : TaskActivity<string, string>;
}
