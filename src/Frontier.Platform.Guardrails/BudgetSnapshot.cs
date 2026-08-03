using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>The aggregated usage for a <see cref="BudgetScopeRef"/> (doc 07 §6 counter-doc shape), returned by <see cref="IBudgetLedger.GetSnapshotAsync"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by BudgetLedger tests.")]
public sealed record BudgetSnapshot(
    BudgetScopeRef Scope,
    long TokensUsed,
    decimal CostGbp,
    int InvocationCount);
