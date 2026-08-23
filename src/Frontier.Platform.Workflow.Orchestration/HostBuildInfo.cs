using System.Reflection;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The host build identity stamped onto every agent activity result (ADR-E15 D1/D3,
/// S13.17) — the pin set's "which code ran this step" answer. Resolved once per process
/// from the executing assembly's informational version (which carries the commit under
/// <c>ContinuousIntegrationBuild</c>), falling back to the assembly version. Stamped
/// activity-side only: a recorded activity result replays its original value, so past
/// steps keep their true attribution across deploys (dtf-determinism skill).
/// </summary>
internal static class HostBuildInfo
{
    /// <summary>The current host build identity, resolved once per process.</summary>
    internal static string Version { get; } = Resolve(
        typeof(HostBuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(HostBuildInfo).Assembly.GetName().Version);

    /// <summary>Pure resolution: a non-blank informational version wins; else the assembly version; last resort <c>"unknown"</c>.</summary>
    internal static string Resolve(string? informationalVersion, Version? assemblyVersion) =>
        !string.IsNullOrWhiteSpace(informationalVersion) ? informationalVersion
        : assemblyVersion?.ToString() ?? "unknown";
}
