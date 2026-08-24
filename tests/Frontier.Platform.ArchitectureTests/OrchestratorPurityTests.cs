using System.Reflection;
using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// Hard invariant 2 — orchestrator bodies are pure — given a mechanism.
/// <para>
/// It had none. <c>GraphOrchestrator</c>'s own doc comment states the rule ("Pure: no
/// <c>DateTime.Now</c>/<c>UtcNow</c>, no GUIDs, no I/O") and the behavioural orchestrator tests
/// are thorough, but nothing failed the build when a body broke it. A non-deterministic
/// orchestrator does not fail loudly: it replays, diverges from the recorded history, and
/// corrupts the execution quietly and later.
/// </para>
/// <para>
/// <b>What this deliberately does not cover.</b> The traversal stops at the platform boundary, so
/// an impure implementation of an injected port — <c>IMcpWriteClassifier</c>, <c>IRollbackPlanner</c>,
/// <c>IResiliencePolicyProvider</c>, all consulted inside the body — is invisible here. Those
/// carry a documented purity contract instead, which is a weaker guarantee honestly stated.
/// Activities are also invisible, and correctly so: they are reached by name through the durable
/// context, and doing I/O is their job.
/// </para>
/// </summary>
public sealed class OrchestratorPurityTests
{
    private static readonly Assembly[] EngineAssemblies =
        [.. GovernedAssemblies.EngineLibraryNames.Select(Assembly.Load)];

    /// <summary>
    /// The guard's own premise: orchestrators are found by interface, so a second orchestrator is
    /// caught automatically — but only if discovery works. An empty or entry-only result would let
    /// every assertion below pass while inspecting nothing, so the shape is pinned by name.
    /// </summary>
    [Fact]
    public void DiscoveryFindsTheOrchestratorsAndWalksPastThem()
    {
        var orchestrators = OrchestratorClosure.OrchestratorTypes(EngineAssemblies);

        Assert.Contains(orchestrators, t => t.Name == "GraphOrchestrator");
        Assert.Contains(orchestrators, t => t.Name == "DispatcherOrchestrator");

        var closure = OrchestratorClosure.Reachable(orchestrators, EngineAssemblies.ToHashSet());
        var walked = closure.Select(m => m.DeclaringType?.Name).ToHashSet(StringComparer.Ordinal);

        // GraphOrchestrator.RunAsync is four delegating lines; the walk it governs is elsewhere.
        // If the closure cannot reach GraphOrchestratorSteps, this guard checks nothing that matters.
        Assert.Contains("GraphOrchestratorSteps", walked);
        Assert.True(closure.Count > orchestrators.Count, $"closure did not expand past its entry points ({closure.Count} methods)");
    }

    /// <summary>
    /// The closure must contain generated <c>MoveNext</c> bodies, not just the stubs that launch them.
    /// <para>
    /// This is a regression pin for a hole the guard shipped with and was caught only by planting a
    /// real violation: an <c>async</c> method's body lives in a compiler-generated state machine that
    /// <em>nothing calls</em>, so a call-following walk sails straight past it. Every meaningful
    /// orchestrator body here is async, which made the guard almost entirely decorative while
    /// reporting success.
    /// </para>
    /// </summary>
    [Fact]
    public void ClosureReachesGeneratedAsyncBodiesNotJustTheirStubs()
    {
        var closure = OrchestratorClosure.Reachable(
            OrchestratorClosure.OrchestratorTypes(EngineAssemblies), EngineAssemblies.ToHashSet());

        var asyncBodies = closure
            .Where(m => m.Name == "MoveNext" && m.DeclaringType?.Name.Contains("RunInitialWalkAsync", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(asyncBodies);
    }

    /// <summary>Nothing an orchestration replays may read state that the history does not carry.</summary>
    [Fact]
    public void OrchestratorBodiesCallNothingNonDeterministic()
    {
        var closure = OrchestratorClosure.Reachable(
            OrchestratorClosure.OrchestratorTypes(EngineAssemblies), EngineAssemblies.ToHashSet());

        var violations = closure
            .SelectMany(method => IlCallScanner.CalledMethods(method).Select(called => (method, called)))
            .Select(pair => (pair.method, pair.called, reason: DeterminismBans.ReasonToRefuse(pair.called)))
            .Where(found => found.reason is not null)
            .Select(found => $"{Describe(found.method)} calls {Describe(found.called)} — {found.reason}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0,
            "Orchestrator bodies must be pure (hard invariant 2). Move the work into an activity, or take "
            + $"the value from TaskOrchestrationContext.{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>
    /// The ban list must be able to fail. A rule that cannot fire is the S13.23 failure — an inert
    /// check whose green result certifies nothing — so the detector is exercised against a body
    /// known to break every category.
    /// </summary>
    [Fact]
    public void TheBanListActuallyFiresOnAnImpureBody()
    {
        var offenders = OrchestratorClosure.DeclaredMethods(typeof(DeliberatelyImpureBody))
            .SelectMany(IlCallScanner.CalledMethods)
            .Select(DeterminismBans.ReasonToRefuse)
            .Where(reason => reason is not null)
            .ToList();

        Assert.Equal(4, offenders.Count);
    }

    internal static string Describe(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    /// <summary>A stand-in orchestrator body, present only so the guard can be shown to reject one.</summary>
    internal static class DeliberatelyImpureBody
    {
        internal static string Run()
        {
            var stamp = DateTime.UtcNow;
            var id = Guid.NewGuid();
            var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
            return $"{stamp:O}-{id}-{ticks}-{File.Exists("x")}";
        }
    }
}
