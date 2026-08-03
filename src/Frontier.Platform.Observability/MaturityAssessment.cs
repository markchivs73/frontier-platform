using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Observability;

/// <summary>
/// Maturity assessment for one (agent_role × engagement_type) pair (doc 11 §6). Contains
/// the current band, hysteresis state (<see cref="PendingTransition"/>), and the evidence
/// statistics that produced it. <see cref="EvidenceQueryRef"/> is a reproducible audit
/// query reference — the numbers can always be re-derived.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised via MaturityEvaluator tests.")]
public sealed record MaturityAssessment(
    string AgentRole,
    string EngagementType,
    MaturityBand? Band,
    int SampleSize,
    DateRange Window,
    decimal ValidatorPassRate,
    decimal HitlRejectionRate,
    decimal OverrideRate,
    MaturityBand? PendingTransition,
    string EvidenceQueryRef);
