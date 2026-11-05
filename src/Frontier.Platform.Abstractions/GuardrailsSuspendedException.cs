namespace Frontier.Platform.Abstractions;

/// <summary>
/// Thrown by Guardrails admission (doc 07 §5, §10) when the fleet kill switch is active: no new
/// invocation is admitted until it is resumed. Per doc 10 §3 this is a permanent failure in the
/// <c>guardrail</c> category, like <see cref="BudgetExceededException"/> — retrying while the switch is
/// thrown burns nothing but time, so Resilience never retries it. The graph walk pauses on the failure
/// (<c>paused_on_failure</c>, classified <c>guardrail</c>); resuming it once the switch is lifted is the
/// consumer's resume route.
/// </summary>
public sealed class GuardrailsSuspendedException : Exception
{
    /// <summary>The message every kill-switch denial carries, naming doc 07 §10's reason code.</summary>
    public const string DefaultMessage = "guardrails_suspended: the fleet kill switch is active, so no new invocation is admitted.";

    /// <summary>Creates the exception with <see cref="DefaultMessage"/>.</summary>
    public GuardrailsSuspendedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a free-text message (CA1032).</summary>
    public GuardrailsSuspendedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a free-text message and inner exception (CA1032).</summary>
    public GuardrailsSuspendedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
