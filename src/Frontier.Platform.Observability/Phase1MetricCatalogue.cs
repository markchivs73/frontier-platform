using System.Diagnostics.Metrics;

namespace Frontier.Platform.Observability;

/// <summary>
/// Compiled-in metric catalogue for Phase 1 (doc 11 §4, ADR-O1). The 15 metrics are
/// grouped by the principle they serve: cache-tier alignment (§4.1), empirical design
/// validation (§4.2), and operational health (§4.3). Implements <see cref="IDisposable"/>
/// because it owns the shared <see cref="IMetricCatalogue.PlatformMeter"/>.
/// </summary>
internal sealed class Phase1MetricCatalogue : IMetricCatalogue, IDisposable
{
    /// <summary>The OTEL meter name shared by all platform instruments (ADR-O1).</summary>
    internal const string MeterName = "Frontier.Platform";

    private static readonly IReadOnlyList<MetricDefinition> Catalogue = BuildCatalogue();

    /// <inheritdoc />
    public IReadOnlyList<MetricDefinition> All => Catalogue;

    /// <inheritdoc />
    public Meter PlatformMeter { get; } = new(MeterName);

    /// <inheritdoc />
    public void Dispose() => PlatformMeter.Dispose();

    internal static IReadOnlyList<MetricDefinition> BuildCatalogue() =>
    [
        // §4.1 — Cache-tier alignment (the canary group)
        new("context.cache.hit_rate",    MetricInstrumentType.ObservableGauge, "ratio",  ["tier"],                          "Cache hit rate by tier; alert on EWMA trend break, not fixed threshold (doc 11 §8)."),
        new("context.tokens",            MetricInstrumentType.Counter,          "token",  ["tier", "cached"],                "Token split: Gate 2 measurement (doc 11 §4.1)."),
        new("context.cache.writes",      MetricInstrumentType.Counter,          "count",  ["tier"],                          "Write-rate spike signals refresh storm or byte instability."),
        new("context.refresh.events",    MetricInstrumentType.Counter,          "count",  ["reason"],                        "Refresh reason correlates with dynamic hit-rate dips."),
        new("context.cost.saved_gbp",    MetricInstrumentType.Counter,          "GBP",    ["engagement_type"],               "Cache-read vs. full-price delta; continuously measures the 40–50% cost-saving claim."),

        // §4.2 — Empirical design validation
        new("validator.outcomes",        MetricInstrumentType.Counter,          "count",  ["validator_id", "status"],        "Check threshold calibration; maturity band input."),
        new("resilience.retries",        MetricInstrumentType.Counter,          "count",  ["reason_code", "model"],          "Retry policy tuning per doc 10 §8."),
        new("hitl.decisions",            MetricInstrumentType.Counter,          "count",  ["gate_kind", "decision"],         "Gate placement: >98% approve = friction; >30% reject = upstream quality signal."),
        new("hitl.time_to_decision",     MetricInstrumentType.Histogram,        "ms",     ["gate_kind"],                     "SLA reality vs. escalation config."),
        new("cascade.size",              MetricInstrumentType.Histogram,        "count",  ["engagement_type"],               "Routinely large cascades suggest over-coupled sections."),
        new("agent.invocation.duration", MetricInstrumentType.Histogram,        "ms",     ["agent_role", "model"],           "Role/model latency fit."),

        // §4.3 — Operational health (RED)
        new("activity.executions",       MetricInstrumentType.Counter,          "count",  ["activity", "status"],            "Activity execution counts by status."),
        new("activity.duration",         MetricInstrumentType.Histogram,        "ms",     ["activity"],                      "Activity execution latency."),
        new("executions.active",         MetricInstrumentType.ObservableGauge,  "count",  ["status"],                        "Active executions by status from isLatest projections."),
        new("recovery.findings",         MetricInstrumentType.Counter,          "count",  ["kind"],                          "Orphaned executions / unhealed flags; healthy platform shows ~0 (C-22)."),
    ];
}
