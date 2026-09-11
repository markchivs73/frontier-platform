using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>
/// The aggregated usage for a <see cref="BudgetScopeRef"/> (doc 07 §6 counter-doc shape), returned by
/// <see cref="IBudgetLedger.GetSnapshotAsync"/>. <see cref="Cost"/> is in <see cref="Currency"/>
/// (ISO 4217, ADR-PA21), which is <c>null</c> while nothing has been recorded at the scope.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by BudgetLedger tests.")]
public sealed record BudgetSnapshot(
    BudgetScopeRef Scope,
    long TokensUsed,
    decimal Cost,
    string? Currency,
    int InvocationCount);
