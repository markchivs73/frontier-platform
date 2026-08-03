using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// A named, layered budget policy (doc 07 §4, §9: platform default → engagement-type
/// default → per-engagement override, most-specific wins per field). Phase 1's
/// <see cref="AdmissionController"/> enforces only <see cref="PerInvocation"/>
/// (doc 07 §5's <c>GrantedMaxOutputTokens</c> shaping); the hierarchical rollup across
/// <see cref="PerExecution"/>/<see cref="PerEngagement"/>, <see cref="SoftThresholdPercent"/>
/// alerting, and <see cref="OnInfrastructureFailure"/> handling are deferred to S6.5 —
/// the fields exist now so the policy shape is frozen and the Phase 1 catalogue
/// (<see cref="Phase1GuardrailPolicyCatalogue"/>) matches doc 07 field-for-field.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by AdmissionController and Phase1GuardrailPolicyCatalogue tests.")]
public sealed record GuardrailPolicy(
    string PolicyId,
    BudgetSpec? PerInvocation,
    BudgetSpec? PerExecution,
    BudgetSpec? PerEngagement,
    int SoftThresholdPercent = 80,
    FailureMode OnInfrastructureFailure = FailureMode.FailOpenWithAudit);
