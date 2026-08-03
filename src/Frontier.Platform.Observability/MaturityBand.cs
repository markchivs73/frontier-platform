namespace Frontier.Platform.Observability;

/// <summary>
/// Maturity band for an (agent_role × engagement_type) pair (doc 11 §6). Computed-not-acted
/// in Phase 1 — bands are displayed and audit-referenced; governance behaviour acting on
/// them (lighter gates for <c>Trusted</c> agents) is a future Stage 9+ decision.
/// </summary>
public enum MaturityBand
{
    /// <summary>Default band; pass rate below <see cref="MaturityThresholds.CalibratedPassRate"/>.</summary>
    Provisional,

    /// <summary>Pass rate ≥ <see cref="MaturityThresholds.CalibratedPassRate"/> and rejection ≤ <see cref="MaturityThresholds.CalibratedRejectionRate"/>.</summary>
    Calibrated,

    /// <summary>Pass rate ≥ <see cref="MaturityThresholds.TrustedPassRate"/> and rejection ≤ <see cref="MaturityThresholds.TrustedRejectionRate"/>.</summary>
    Trusted,
}
