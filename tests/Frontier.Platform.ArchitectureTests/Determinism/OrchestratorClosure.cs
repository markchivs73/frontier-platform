using System.Reflection;
using System.Runtime.CompilerServices;

namespace Frontier.Platform.ArchitectureTests.Determinism;

/// <summary>
/// The set of methods that actually run inside an orchestration replay: every orchestrator entry
/// point, plus everything it transitively calls that this platform owns.
/// <para>
/// The closure is the point. <c>GraphOrchestrator.RunAsync</c> is four delegating lines — a guard
/// that stopped at the entry type would pass while inspecting almost nothing, since the walk it
/// governs lives in <c>GraphOrchestratorSteps</c>. The traversal stops at the platform boundary:
/// activities are reached by name through the durable context, never by a call instruction, so
/// their I/O is correctly invisible here.
/// </para>
/// </summary>
internal static class OrchestratorClosure
{
    /// <summary>Types implementing a Durable Task orchestration interface — the replayed entry points.</summary>
    internal static IReadOnlyList<Type> OrchestratorTypes(IEnumerable<Assembly> assemblies) =>
        [.. assemblies.SelectMany(a => a.GetTypes()).Where(IsOrchestrator).OrderBy(t => t.FullName, StringComparer.Ordinal)];

    internal static bool IsOrchestrator(Type type) =>
        !type.IsAbstract && !type.IsInterface
        && type.GetInterfaces().Any(i => i.Namespace?.StartsWith("Microsoft.DurableTask", StringComparison.Ordinal) == true
                                         && i.Name.Contains("Orchestrat", StringComparison.Ordinal));

    /// <summary>Every method reachable from <paramref name="entryTypes"/> without leaving <paramref name="owned"/>.</summary>
    internal static IReadOnlyCollection<MethodBase> Reachable(IEnumerable<Type> entryTypes, IReadOnlySet<Assembly> owned)
    {
        var seen = new HashSet<MethodBase>(entryTypes.SelectMany(DeclaredMethods));
        var queue = new Queue<MethodBase>(seen);

        while (queue.Count > 0)
        {
            Expand(queue.Dequeue(), owned, seen, queue);
        }

        return seen;
    }

    internal static void Expand(MethodBase method, IReadOnlySet<Assembly> owned, HashSet<MethodBase> seen, Queue<MethodBase> queue)
    {
        foreach (var called in IlCallScanner.CalledMethods(method).Concat(StateMachineBody(method)))
        {
            if (IsOwned(called, owned) && seen.Add(called))
            {
                queue.Enqueue(called);
            }
        }
    }

    /// <summary>
    /// An <c>async</c> or iterator method's real body, which lives in a generated nested type that
    /// <em>no call instruction points at</em> — the stub hands the state machine to a builder.
    /// Following calls alone therefore walks past every asynchronous body in the closure, which is
    /// nearly all of them here. Verified the hard way: with this missing, a <c>DateTime.UtcNow</c>
    /// planted in <c>GraphOrchestratorSteps</c> did not fail the guard.
    /// </summary>
    internal static IEnumerable<MethodBase> StateMachineBody(MethodBase method) =>
        method.GetCustomAttributes()
            .OfType<StateMachineAttribute>()
            .Select(a => a.StateMachineType)
            .SelectMany(DeclaredMethods);

    internal static bool IsOwned(MethodBase method, IReadOnlySet<Assembly> owned) =>
        method.DeclaringType is { } declaring && owned.Contains(declaring.Assembly);

    /// <summary>
    /// A type's own methods and constructors, plus those of its nested types — which is where the
    /// compiler puts an <c>async</c> body, and therefore where the orchestrator's real code lives.
    /// </summary>
    internal static IEnumerable<MethodBase> DeclaredMethods(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var own = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
        return own.Concat(type.GetNestedTypes(all).SelectMany(DeclaredMethods));
    }
}
