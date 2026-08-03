using Frontier.Platform.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Observability;

/// <summary>
/// DI registration for Frontier.Platform.Observability (S3.4, S4.7c).
/// Only Host calls this (library-boundaries: composition root only).
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the full S6.8 Observability surface (doc 11): context metrics emitter,
    /// <see cref="IMetricCatalogue"/> (Phase 1 catalogue, 15 metrics, shared
    /// <see cref="System.Diagnostics.Metrics.Meter"/>), <see cref="IEmpiricalQueryService"/>
    /// (Phase 1 stub — reads <c>metrics-aggregates</c> when S7+ aggregation layer exists),
    /// <see cref="IMaturityTracker"/> (Phase 1 stub), <see cref="IExecutionMonitorFeed"/>
    /// (Phase 1 stub — real span-processor tap is Stage 9/doc 19), the
    /// <see cref="OtelPipelineCheck"/> boot invariant (doc 12 §6), and the
    /// <see cref="IRecoveryFindingsRecorder"/> (C-22) consumed by the recovery sweep (S6.11b).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration, registered so <see cref="OtelPipelineCheck"/> can resolve <see cref="IConfiguration"/>.</param>
    /// <returns>The service collection (for chaining).</returns>
    public static IServiceCollection AddFrontierObservability(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddSingleton<IContextMetricsEmitter, InMemoryContextMetricsEmitter>()
            .AddSingleton<IMetricCatalogue, Phase1MetricCatalogue>()
            .AddSingleton<IEmpiricalQueryService, Phase1EmpiricalQueryService>()
            .AddSingleton<IMaturityTracker, Phase1MaturityTracker>()
            .AddSingleton<IExecutionMonitorFeed, Phase1ExecutionMonitorFeed>()
            .AddSingleton(configuration)
            .AddSingleton<IStartupCheck, OtelPipelineCheck>()
            .AddSingleton<IRecoveryFindingsRecorder, RecoveryFindingsRecorder>();
    }
}
