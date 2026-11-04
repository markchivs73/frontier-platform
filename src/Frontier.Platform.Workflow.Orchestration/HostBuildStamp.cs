using System.Reflection;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>Builds an <see cref="IHostBuildStamp"/> from the assembly the consumer names as its worker build (S13.182).</summary>
public static class HostBuildStamp
{
    /// <summary>
    /// The stamp of <paramref name="assembly"/>, resolved once: its informational version when
    /// non-blank, else its assembly version, else <c>"unknown"</c>. Pass the worker's entry
    /// assembly (for example <c>typeof(Program).Assembly</c>), never a platform one; a name check
    /// cannot tell the two apart, so this is not enforced (ADR-PA44).
    /// </summary>
    public static IHostBuildStamp FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new FixedHostBuildStamp(Resolve(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version));
    }

    /// <summary>Pure resolution: a non-blank informational version wins; else the assembly version; last resort <c>"unknown"</c>.</summary>
    internal static string Resolve(string? informationalVersion, Version? assemblyVersion) =>
        !string.IsNullOrWhiteSpace(informationalVersion) ? informationalVersion
        : assemblyVersion?.ToString() ?? "unknown";

    /// <summary>A stamp resolved once and held for the process.</summary>
    private sealed class FixedHostBuildStamp(string version) : IHostBuildStamp
    {
        /// <inheritdoc />
        public string Version { get; } = version;
    }
}
