namespace Frontier.Platform.Guardrails;

/// <summary>The four hierarchical budget scopes (doc 07 §2 rule 1). Phase 1's <see cref="BudgetLedger"/> aggregates <see cref="Execution"/> and <see cref="Engagement"/>; <see cref="Fleet"/> rollup is the S6.5 change-feed aggregator (doc 07 §6).</summary>
public enum BudgetScopeKind
{
    /// <summary>A single agent invocation, identified by its correlation id.</summary>
    Invocation,

    /// <summary>One workflow execution.</summary>
    Execution,

    /// <summary>All executions for an engagement.</summary>
    Engagement,

    /// <summary>The whole fleet — alert-only, never an admission scope (doc 07 §6).</summary>
    Fleet,
}
