using System.Reflection;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;

namespace Frontier.Platform.Workflow.Orchestration.Tests.ActivityShapes;

/// <summary>
/// Finds every activity the platform's orchestrators schedule. Platform implementations are found by
/// reflection over the Orchestration assembly and the <c>Frontier.Platform.*</c> assemblies it
/// references; one elsewhere is caught only through the name-coverage test, and only when it has a
/// <see cref="WorkflowActivityNames"/> constant. Activities the platform calls but the consumer
/// implements are listed by hand, because their wire types are platform contracts even though their
/// code is not; the name-coverage test keeps that list complete against
/// <see cref="WorkflowActivityNames"/>, and <c>ConsumerActivityCallSiteTests</c> keeps its types
/// equal to what the call sites pass and expect.
/// </summary>
internal static class ActivityCatalogue
{
    /// <summary>The consumer-implemented activities, with the types <c>GraphOrchestratorSteps</c> passes and expects.</summary>
    public static readonly IReadOnlyList<ActivityImplementation> ConsumerImplemented =
    [
        new(WorkflowActivityNames.EvaluateCascadeActivity, ActivityImplementation.Consumer, typeof(CascadeActivityRequest), typeof(CascadeActivityResponse), null),
        new(WorkflowActivityNames.SnapshotStateActivity, ActivityImplementation.Consumer, typeof(ExecutionSnapshot), typeof(SnapshotActivityResponse), null),
        new(WorkflowActivityNames.ArtifactStateActivity, ActivityImplementation.Consumer, typeof(ArtifactStateActivityRequest), typeof(ArtifactStateActivityResponse), null),
        new(WorkflowActivityNames.RestoreArtifactsActivity, ActivityImplementation.Consumer, typeof(ArtifactRestoreActivityRequest), typeof(ArtifactStateActivityResponse), null),
    ];

    /// <summary>Every <see cref="TaskActivity{TInput, TOutput}"/> in the platform assemblies.</summary>
    public static IReadOnlyList<ActivityImplementation> PlatformImplemented() =>
        [.. PlatformAssemblies().SelectMany(a => a.GetTypes()).Select(Describe).OfType<ActivityImplementation>().OrderBy(a => a.Name, StringComparer.Ordinal)];

    /// <summary>The shapes the implementations serving <paramref name="generation"/> produce, given the snapshot's <paramref name="current"/> generation.</summary>
    public static IReadOnlyList<ActivityShape> Shapes(int generation, int current) => ShapesOf(PlatformImplemented(), generation, current);

    /// <summary>
    /// The shapes the <paramref name="platform"/> implementations serving <paramref name="generation"/>
    /// produce, plus the consumer-implemented ones when it is the <paramref name="current"/> generation:
    /// the platform cannot keep an older generation of code it does not ship.
    /// </summary>
    public static IReadOnlyList<ActivityShape> ShapesOf(IEnumerable<ActivityImplementation> platform, int generation, int current) =>
        [.. platform.Concat(generation == current ? ConsumerImplemented : [])
            .Where(a => a.Serves(generation, current))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => a.ToShape())];

    /// <summary>The generation a <see cref="DurableTaskAttribute"/> registers its type under, or <see langword="null"/> when it sets none.</summary>
    /// <remarks>
    /// Read by name: <c>Version</c> arrives with Durable Task SDK v1.25.0 (ADR-E15 amendment), and this
    /// repo is on 1.24.2, where every implementation is unversioned and so serves the current generation.
    /// </remarks>
    public static int? GenerationOf(Attribute attribute) =>
        attribute.GetType().GetProperty("Version")?.GetValue(attribute) is string { Length: > 0 } version
            ? int.Parse(version, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Describes <paramref name="type"/> when it is a concrete activity, else <see langword="null"/>.</summary>
    public static ActivityImplementation? Describe(Type type)
    {
        if (type.IsAbstract || ActivityBase(type) is not { } activity)
        {
            return null;
        }

        var attribute = type.GetCustomAttribute<DurableTaskAttribute>();
        var arguments = activity.GetGenericArguments();
        var name = attribute?.Name.Name ?? type.Name;
        return new(name, ActivityImplementation.Platform, arguments[0], arguments[1], attribute is null ? null : GenerationOf(attribute));
    }

    private static Type? ActivityBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(TaskActivity<,>))
            {
                return current;
            }
        }

        return null;
    }

    private static IEnumerable<Assembly> PlatformAssemblies()
    {
        var root = typeof(GraphOrchestrator).Assembly;
        return root.GetReferencedAssemblies()
            .Where(name => name.Name!.StartsWith("Frontier.Platform.", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Prepend(root);
    }
}
