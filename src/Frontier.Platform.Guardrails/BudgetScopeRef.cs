using System.Diagnostics.CodeAnalysis;

namespace Frontier.Platform.Guardrails;

/// <summary>Identifies a budget scope to query (doc 07 §6): <paramref name="Id"/> is the execution/engagement/correlation id matching <paramref name="Kind"/>; ignored for <see cref="BudgetScopeKind.Fleet"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Plain data record; exercised by BudgetLedger tests.")]
public sealed record BudgetScopeRef(BudgetScopeKind Kind, string Id);
