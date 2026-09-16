namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// A governance decision could not be stored (ADR-PA32): the concurrency retries were exhausted, or
/// the store refused the write for a reason that is not a conflict. Distinct from
/// <see cref="Abstractions.ContractViolationException"/>, which means the decision itself was never
/// legal — this one means a legal decision did not land, so a consumer answers 503 and the caller
/// may retry.
/// </summary>
public sealed class MappingGovernanceException : Exception
{
    /// <summary>Creates an empty exception (CA1032).</summary>
    public MappingGovernanceException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    public MappingGovernanceException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public MappingGovernanceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
