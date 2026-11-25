using Frontier.Platform.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Boot check (consumer S13.146): both ports a dispatcher's generation boundary needs are registered. Without them the
/// container still builds and the first rollover, possibly days into a dispatcher's life, fails or silently keeps stale
/// settings. It probes the container with <see cref="IServiceProviderIsService"/> rather than injecting the ports, so a
/// missing port fails this one check instead of making every <see cref="IStartupCheck"/> unresolvable.
/// </summary>
internal sealed class DispatcherPortsCheck(IServiceProviderIsService services) : IStartupCheck
{
    /// <inheritdoc />
    public string Name => "DispatcherPorts";

    /// <inheritdoc />
    public Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(services.IsService(typeof(IDispatcherVersionResolver)), services.IsService(typeof(IDispatcherGenerationSettingsResolver))));

    /// <summary>Passes when both ports are registered; otherwise names each one that is missing.</summary>
    internal static StartupCheckResult Evaluate(bool hasVersionResolver, bool hasSettingsResolver)
    {
        string[] missing =
        [
            .. hasVersionResolver ? Array.Empty<string>() : [nameof(IDispatcherVersionResolver)],
            .. hasSettingsResolver ? Array.Empty<string>() : [nameof(IDispatcherGenerationSettingsResolver)],
        ];

        return missing.Length == 0
            ? StartupCheckResult.Pass()
            : StartupCheckResult.Fail($"The dispatcher's generation boundary needs {string.Join(" and ", missing)} registered by the composition root (ADR-PA37). Without it the first rollover fails.");
    }
}
