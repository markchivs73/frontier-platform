namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The worker build identity stamped onto every agent and tool activity result (ADR-E15 D1/D3,
/// S13.17, S13.182) — the pin set's "which code ran this step" answer.
/// <para>
/// <b>A port the consumer supplies, deliberately not DI-registered by the engine.</b> The build
/// that ran a step is the <em>worker's</em> build, which only the worker's composition root knows.
/// A platform default read its own assembly, so every deploy on one platform version stamped the
/// same value (S13.182); a default here would bring that silent wrong answer back. Register one
/// with <see cref="HostBuildStamp.FromAssembly"/> over the worker's entry assembly.
/// </para>
/// <para>
/// Read activity-side only, so a recorded activity result replays the value it recorded and past
/// steps keep their true attribution across deploys (dtf-determinism skill). Nothing on this
/// interface may appear in an orchestrator body (hard invariant 2).
/// </para>
/// </summary>
public interface IHostBuildStamp
{
    /// <summary>The worker build identity — its informational version, which carries the commit under <c>ContinuousIntegrationBuild</c>.</summary>
    string Version { get; }
}
