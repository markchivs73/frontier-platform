using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The result of <see cref="IAdmissionController.AdmitAsync"/> (doc 07 §4). On
/// <see cref="AdmissionResult.Deny"/> the invocation pipeline (S4.2) throws
/// <c>BudgetExceededException</c> — a permanent failure (doc 10 §3) — carrying
/// <see cref="Reason"/>. <see cref="GrantedMaxOutputTokens"/> is the
/// shape-don't-truncate mechanism (doc 07 §4): when remaining budget is positive but
/// smaller than the node requested, admission proceeds with a lower provider-side
/// <c>max_tokens</c> rather than denying.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by AdmissionController tests.")]
public sealed record AdmissionDecision(
    AdmissionResult Result,
    string? Reason,
    long? GrantedMaxOutputTokens,
    TimeSpan? RetryAfter);
