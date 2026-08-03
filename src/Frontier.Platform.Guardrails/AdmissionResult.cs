namespace Frontier.Platform.Guardrails;

/// <summary>The outcome of an admission check (doc 07 §4 <c>AdmissionDecision.Result</c>).</summary>
public enum AdmissionResult
{
    /// <summary>The invocation may proceed unchanged.</summary>
    Proceed,

    /// <summary>The invocation may proceed, but <see cref="AdmissionDecision.GrantedMaxOutputTokens"/> shapes the output cap below what was requested.</summary>
    ProceedWithWarning,

    /// <summary>The invocation must not proceed — a hard budget ceiling would be breached (permanent failure, doc 10 §3).</summary>
    Deny,

    /// <summary>The invocation must wait <see cref="AdmissionDecision.RetryAfter"/> before retrying (rate-limiter admission, S6.5 — transient).</summary>
    Deferred,
}
