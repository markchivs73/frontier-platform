using System.Reflection;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// S13.185, the S10.7 deliverable (ADR-E15 amendment 2026-11-03, decision 5): the committed
/// activity-shape snapshot decides when the compatibility generation must be bumped. Within a
/// generation, shapes evolve additively; removing, renaming or reshaping a member, adding a
/// required one, or removing an activity needs a new generation, with the old generation's
/// implementations still registered beside the new ones.
/// <para>
/// To accept an additive change, run this class with <c>FRONTIER_ACTIVITY_SHAPES=update</c> and
/// commit the rewritten snapshot. To start a new generation after a breaking change, keep the old
/// implementations registered under their generation's version and run it with
/// <c>FRONTIER_ACTIVITY_SHAPES=bump</c>. The decisions live in <see cref="ActivityShapeRules"/>, whose
/// tests drive them with stand-in catalogues; this class applies them to the committed file.
/// </para>
/// </summary>
public sealed class ActivityShapeSnapshotTests
{
    private static readonly ActivityShapeSnapshot Committed = ActivityShapeStore.Load(ActivityShapeStore.CommittedPath);

    [Fact]
    public void Code_MatchesTheCurrentGeneration()
    {
        var mode = Environment.GetEnvironmentVariable(ActivityShapeStore.ModeVariable);
        if (ActivityShapeRules.Rewrite(Committed, mode, ActivityCatalogue.Shapes) is { } rewritten)
        {
            ActivityShapeStore.WriteSource(rewritten, AppContext.BaseDirectory);
            return;
        }

        var changes = ActivityShapeRules.Drift(Committed, ActivityCatalogue.Shapes);
        Assert.True(changes.Count == 0, DriftMessage(Committed.Current.Generation, changes));
    }

    [Fact]
    public void CommittedSnapshot_IsCompatibleWithTheBaseBranch()
    {
        var baselinePath = Environment.GetEnvironmentVariable(ActivityShapeStore.BaselineVariable);
        var baseline = string.IsNullOrEmpty(baselinePath) ? Committed : ActivityShapeStore.Load(baselinePath);
        var incompatible = ActivityShapeRules.IncompatibleEdits(Committed, baseline);

        Assert.True(incompatible.Count == 0, "The snapshot was edited incompatibly with the base branch; a breaking change needs a new generation:\n" + string.Join('\n', incompatible));
    }

    [Fact]
    public void OlderGenerations_StayRegistered()
    {
        var missing = ActivityShapeRules.Unregistered(Committed, ActivityCatalogue.Shapes);

        Assert.True(missing.Count == 0, "An older generation's implementations must stay registered under its version until it drains:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Generations_AscendAndAtMostTwoAreLive()
    {
        var numbers = Committed.Generations.Select(g => g.Generation).ToList();

        Assert.Equal(numbers.Order(), numbers);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.InRange(numbers.Count, 1, 2);
    }

    [Fact]
    public void EveryScheduledActivity_IsInTheCatalogue()
    {
        string[] orchestrations = [WorkflowActivityNames.GraphOrchestrator, WorkflowActivityNames.DispatcherOrchestrator];
        var declared = typeof(WorkflowActivityNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Except(orchestrations)
            .Order(StringComparer.Ordinal);
        var catalogued = ActivityCatalogue.PlatformImplemented().Concat(ActivityCatalogue.ConsumerImplemented).Select(a => a.Name).Order(StringComparer.Ordinal);

        Assert.Equal(declared, catalogued);
    }

    private static string DriftMessage(int generation, IReadOnlyList<ShapeChange> changes) =>
        $"Activity shapes differ from generation {generation} in {ActivityShapeStore.RepositoryPath}:\n{string.Join('\n', changes)}\n"
        + (changes.Any(c => c.Breaking)
            ? "A breaking change needs a new generation: keep the old implementations registered under their version, then run with FRONTIER_ACTIVITY_SHAPES=bump."
            : "Additive only: run with FRONTIER_ACTIVITY_SHAPES=update and commit the snapshot.");
}
