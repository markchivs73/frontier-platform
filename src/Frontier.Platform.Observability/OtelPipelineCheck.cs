using Frontier.Platform.Serialization;
using Microsoft.Extensions.Configuration;

namespace Frontier.Platform.Observability;

/// <summary>
/// Boot check (doc 12 §6/§7): the OTLP collector endpoint, when configured via
/// <see cref="OtlpEndpointConfigurationKey"/> (the same key consumed by
/// <c>Frontier.Reason.Workflow.ServiceDefaults.AddOtlpExporterIfConfigured</c>), must be a
/// well-formed absolute URI — a typo here would silently drop every span and metric (doc
/// 12 §6 "Catches: Silent telemetry loss"). An unset endpoint is a valid local-dev state
/// (no exporter configured) and passes.
///
/// Doc 12 §6's <c>OtelPipelineCheck</c> row also covers "audit staging container
/// writable": that pipeline (doc 05 §9, doc 12 §7) is config-managed infrastructure
/// alongside the heads, not application code, and does not exist yet. This check covers
/// collector-endpoint validity only until that pipeline lands.
/// </summary>
internal sealed class OtelPipelineCheck(IConfiguration configuration) : IStartupCheck
{
    /// <summary>The configuration key holding the OTLP collector endpoint, if any.</summary>
    internal const string OtlpEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <inheritdoc />
    public string Name => "OtelPipeline";

    /// <inheritdoc />
    public Task<StartupCheckResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Evaluate(configuration[OtlpEndpointConfigurationKey]));

    /// <summary>Passes when <paramref name="otlpEndpoint"/> is unset, or set to a well-formed absolute URI.</summary>
    internal static StartupCheckResult Evaluate(string? otlpEndpoint)
    {
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            return StartupCheckResult.Pass();
        }

        return Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out _)
            ? StartupCheckResult.Pass()
            : StartupCheckResult.Fail($"{OtlpEndpointConfigurationKey} '{otlpEndpoint}' is not a valid absolute URI; telemetry would be silently dropped (doc 12 §6, §7).");
    }
}
