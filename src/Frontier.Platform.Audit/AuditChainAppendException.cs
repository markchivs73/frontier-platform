namespace Frontier.Platform.Audit;

/// <summary>
/// The signed record did not land (ADR-PA31): the conditional append kept losing the engagement's
/// chain head to concurrent closes until the configured attempts were exhausted, or the batch failed
/// for a reason that is not a concurrency conflict. Fail closed — the caller must treat the audit as
/// unwritten, never as written.
/// </summary>
public sealed class AuditChainAppendException : Exception
{
    /// <summary>Creates an empty exception (CA1032).</summary>
    public AuditChainAppendException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public AuditChainAppendException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public AuditChainAppendException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
