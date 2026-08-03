namespace Frontier.Platform.Abstractions;

/// <summary>
/// Thrown by the agent-invocation pipeline (doc 07 §5) when an
/// <c>AdmissionDecision</c> from the Guardrails library is <c>Deny</c>. Per doc 10 §3,
/// this is a permanent failure classification — retrying into the same budget ceiling
/// burns nothing but time, so Resilience must never retry it. Recovery is a human
/// raising the breached budget and resuming the paused execution.
/// </summary>
public sealed class BudgetExceededException : Exception
{
    /// <summary>Creates an empty exception (CA1032); prefer the (policyId, reason) constructor.</summary>
    public BudgetExceededException()
        : this(string.Empty, string.Empty)
    {
    }

    /// <summary>Creates the exception with a free-text message (CA1032); prefer the (policyId, reason) constructor.</summary>
    public BudgetExceededException(string message)
        : base(message)
    {
        PolicyId = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the exception with a free-text message and inner exception (CA1032); prefer the (policyId, reason) constructor.</summary>
    public BudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
        PolicyId = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Creates the exception for the breached guardrail policy and the admission reason.</summary>
    public BudgetExceededException(string policyId, string reason)
        : base($"{policyId}: {reason}")
    {
        PolicyId = policyId;
        Reason = reason;
    }

    /// <summary>The id of the guardrail policy (doc 07 §4 <c>GuardrailPolicy.PolicyId</c>) whose budget was exceeded.</summary>
    public string PolicyId { get; }

    /// <summary>The admission decision's reason text (doc 07 §4 <c>AdmissionDecision.Reason</c>).</summary>
    public string Reason { get; }
}
