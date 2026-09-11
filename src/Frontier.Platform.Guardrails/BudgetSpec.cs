using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// A single budget ceiling within a <see cref="GuardrailPolicy"/> (doc 07 §4). Any
/// field left <c>null</c> is unbounded at that scope. <see cref="MaxAgentInvocations"/>
/// is the runaway-loop fuse (doc 07 §5: a cascade-regeneration ping-pong or buggy
/// <c>LoopNode</c> surfaces here as a hard, attributable stop). <see cref="MaxCost"/> is in
/// <see cref="Currency"/> (ISO 4217); a ceiling is only compared with an estimate in the same
/// currency (ADR-PA21).
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by AdmissionController and Phase1GuardrailPolicyCatalogue tests.")]
public sealed record BudgetSpec(
    long? MaxTokens,
    decimal? MaxCost,
    string? Currency,
    int? MaxAgentInvocations);
