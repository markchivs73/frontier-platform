namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>The shapes the implementations serving a generation produce, given the snapshot's current generation.</summary>
internal delegate IReadOnlyList<ActivityShape> ShapeSource(int generation, int current);

/// <summary>
/// The decisions <see cref="ActivityShapeSnapshotTests"/> asserts, taken out of the test methods so
/// each can be driven with a stand-in catalogue and snapshot: drift from the current generation,
/// the rewrite <c>FRONTIER_ACTIVITY_SHAPES</c> asks for, edits incompatible with the base branch,
/// and older generations whose implementations are no longer registered.
/// </summary>
internal static class ActivityShapeRules
{
    /// <summary>The mode that accepts additive changes into the current generation.</summary>
    public const string Update = "update";

    /// <summary>The mode that starts a new generation.</summary>
    public const string Bump = "bump";

    /// <summary>Every difference from <paramref name="committed"/>'s current generation to what <paramref name="code"/> produces for it.</summary>
    public static IReadOnlyList<ShapeChange> Drift(ActivityShapeSnapshot committed, ShapeSource code) =>
        ShapeComparer.Compare(committed.Current.Activities, code(committed.Current.Generation, committed.Current.Generation));

    /// <summary>
    /// The snapshot <paramref name="mode"/> asks for: <see cref="Bump"/> appends the next generation;
    /// <see cref="Update"/> replaces the current one, but only when the drift is additive. Otherwise
    /// <see langword="null"/>, and the drift is left for the test to report.
    /// </summary>
    public static ActivityShapeSnapshot? Rewrite(ActivityShapeSnapshot committed, string? mode, ShapeSource code) => mode switch
    {
        Bump => With([.. committed.Generations, Generation(committed.Current.Generation + 1, code)]),
        Update when !Drift(committed, code).Any(c => c.Breaking) => With([.. committed.Generations.SkipLast(1), Generation(committed.Current.Generation, code)]),
        _ => null,
    };

    /// <summary>Why <paramref name="committed"/> is not a compatible successor of <paramref name="baseline"/>; empty when it is.</summary>
    public static IReadOnlyList<string> IncompatibleEdits(ActivityShapeSnapshot committed, ActivityShapeSnapshot baseline)
    {
        IEnumerable<string> dropped = committed.Find(baseline.Current.Generation) is null
            ? [$"dropped generation {baseline.Current.Generation}, the base branch's current one"]
            : [];
        var breaking = baseline.Generations
            .Select(old => (old, now: committed.Find(old.Generation)))
            .Where(pair => pair.now is not null)
            .SelectMany(pair => ShapeComparer.Compare(pair.old.Activities, pair.now!.Activities).Where(c => c.Breaking).Select(c => $"generation {pair.old.Generation}: {c}"));
        return [.. dropped.Concat(breaking)];
    }

    /// <summary>Each difference between an older generation's platform shapes and what the implementations still registered for it produce.</summary>
    public static IReadOnlyList<string> Unregistered(ActivityShapeSnapshot committed, ShapeSource code) =>
        [.. committed.Generations.SkipLast(1).SelectMany(older => ShapeComparer.Compare(
                PlatformOnly(older.Activities),
                PlatformOnly(code(older.Generation, committed.Current.Generation)))
            .Select(c => $"generation {older.Generation}: {c}"))];

    /// <summary>The generation numbered <paramref name="number"/>, as <paramref name="code"/> produces it.</summary>
    public static GenerationShapes Generation(int number, ShapeSource code) =>
        new() { Generation = number, Activities = code(number, number) };

    private static ActivityShapeSnapshot With(IReadOnlyList<GenerationShapes> generations) => new() { Generations = generations };

    private static IEnumerable<ActivityShape> PlatformOnly(IEnumerable<ActivityShape> shapes) =>
        shapes.Where(a => a.ImplementedBy == ActivityImplementation.Platform);
}
