using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Observability;

/// <summary>
/// Versioned thresholds for maturity band computation (doc 11 §6). Defaults are data, not
/// code — they will be tuned with real-world evidence; the threshold record is in versioned
/// config so the tuning is governed and auditable (ADR-O1).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised via MaturityEvaluator tests.")]
public sealed record MaturityThresholds(
    decimal TrustedPassRate,
    decimal TrustedRejectionRate,
    decimal CalibratedPassRate,
    decimal CalibratedRejectionRate,
    int MinimumSample,
    int EvaluationWindowDays)
{
    /// <summary>Default Phase 1 thresholds per doc 11 §6.</summary>
    public static readonly MaturityThresholds Default = new(
        TrustedPassRate: 0.90m,
        TrustedRejectionRate: 0.05m,
        CalibratedPassRate: 0.75m,
        CalibratedRejectionRate: 0.15m,
        MinimumSample: 20,
        EvaluationWindowDays: 90);
}
