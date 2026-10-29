namespace Frontier.Platform.Contracts;

/// <summary>
/// The outcome of a registration check. <see cref="Snapshot"/> is set exactly when no finding is an
/// error; warnings travel with an accepted contract so the registry can show them on its record.
/// </summary>
/// <param name="Snapshot">The snapshot to store, hashed, when the contract is accepted.</param>
/// <param name="Findings">Every finding, errors first, each group in document order.</param>
public sealed record ContractRegistrationResult(Abstractions.ContractSnapshot? Snapshot, IReadOnlyList<ContractFinding> Findings)
{
    /// <summary>Whether the contract may be registered.</summary>
    public bool IsAccepted => Snapshot is not null;
}
