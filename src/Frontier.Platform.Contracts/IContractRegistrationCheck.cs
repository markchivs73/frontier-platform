namespace Frontier.Platform.Contracts;

/// <summary>
/// Decides whether a contract may be registered (ADR-E27 D4, D5, D10): the ref, the schema's dialect
/// and references, that it compiles, the legibility lint, that every example satisfies it, and
/// compatibility with the earlier minors of its major. Pure: it reads nothing and stores nothing.
/// The registry supplies the earlier versions and stores what is accepted.
/// </summary>
public interface IContractRegistrationCheck
{
    /// <summary>Checks <paramref name="request"/> against <paramref name="registered"/>.</summary>
    /// <param name="request">The contract submitted.</param>
    /// <param name="registered">Every version already registered under the same id. Versions of other majors are ignored.</param>
    ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<Abstractions.ContractSnapshot> registered);
}
