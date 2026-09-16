namespace Frontier.Platform.Abstractions;

/// <summary>
/// Thrown by <see cref="IVersionedContract.Validate"/> when a contract instance is not
/// well-formed. Per the two-loop failure model (doc 09), this is a permanent failure
/// classification — Resilience must never retry it.
/// <para>
/// <b>Open for derivation (ADR-PA34), deliberately.</b> A library whose refusals a caller must
/// tell apart — to map 400 from 403 from 404 from 409 — derives a typed refusal from this type
/// rather than declaring a parallel hierarchy. Deriving keeps the permanent classification intact
/// everywhere it is already made: <c>FailureClassifier</c>'s type pattern and DTF's
/// <c>TaskFailureDetails.IsCausedBy&lt;T&gt;</c> both honour base types, and every existing
/// <c>catch (ContractViolationException)</c> keeps catching. A refusal that did <i>not</i> derive
/// from this type would silently become retryable, which is the failure this note exists to prevent.
/// </para>
/// </summary>
public class ContractViolationException : Exception
{
    /// <summary>Creates an empty exception (CA1032); prefer the (contractType, violations) constructor.</summary>
    public ContractViolationException()
        : this(string.Empty, [])
    {
    }

    /// <summary>Creates the exception with a free-text message (CA1032); prefer the (contractType, violations) constructor.</summary>
    public ContractViolationException(string message)
        : base(message)
    {
        ContractType = string.Empty;
        Violations = [];
    }

    /// <summary>Creates the exception with a free-text message and inner exception (CA1032); prefer the (contractType, violations) constructor.</summary>
    public ContractViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
        ContractType = string.Empty;
        Violations = [];
    }

    /// <summary>Creates the exception for the given contract type and its violations.</summary>
    public ContractViolationException(string contractType, IReadOnlyList<string> violations)
        : base($"{contractType}: {string.Join("; ", violations)}")
    {
        ContractType = contractType;
        Violations = violations;
    }

    /// <summary>The name of the contract type that failed validation.</summary>
    public string ContractType { get; }

    /// <summary>The individual structural/semantic violations found.</summary>
    public IReadOnlyList<string> Violations { get; }
}
