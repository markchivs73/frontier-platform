namespace Frontier.Platform.Guardrails;

/// <summary>
/// How <see cref="IAdmissionController"/> behaves when the guardrail infrastructure
/// itself (the budget ledger) is unreachable (doc 07 §2 rule 3, §10).
/// </summary>
public enum FailureMode
{
    /// <summary>Admit and emit a <c>guardrail_bypass</c> audit event + alarm — the commercial-platform default (doc 07 §2 rule 3).</summary>
    FailOpenWithAudit,

    /// <summary>Deny admission (transient — the store may recover). Regulated-client override.</summary>
    FailClosed,
}
