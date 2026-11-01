namespace Frontier.Platform.Contracts;

/// <summary>
/// Decides whether a contract may be registered (ADR-E27 D4, D5, D10): the ref, the schema's dialect
/// and references, that it compiles, the legibility lint, that every example satisfies it, and
/// compatibility with the earlier minors of its major. Pure: it reads nothing and stores nothing.
/// The registry supplies the earlier versions and stores what is accepted. A rule's cost is checked
/// against <see cref="Abstractions.ContractBudgets.MaxRuleCost"/> here and nowhere else (ADR-E27 D2,
/// ADR-PA43), and the examples run under the same record's iteration budget.
/// </summary>
public interface IContractRegistrationCheck
{
    /// <summary>Checks <paramref name="request"/> against <paramref name="registered"/>, under the budgets <see cref="ContractsOptions"/> configures.</summary>
    /// <param name="request">The contract submitted.</param>
    /// <param name="registered">Every version already registered under the same id. Versions of other majors are ignored.</param>
    ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<Abstractions.ContractSnapshot> registered);

    /// <summary>Checks <paramref name="request"/> against <paramref name="registered"/>, under <paramref name="budgets"/>: the governed record in force when it is registered.</summary>
    /// <param name="request">The contract submitted.</param>
    /// <param name="registered">Every version already registered under the same id. Versions of other majors are ignored.</param>
    /// <param name="budgets">The cost ceiling every rule is held to, and the iteration budget the examples run under.</param>
    /// <exception cref="Abstractions.ContractViolationException"><paramref name="budgets"/> is out of range.</exception>
    ContractRegistrationResult Check(ContractRegistrationRequest request, IReadOnlyList<Abstractions.ContractSnapshot> registered, Abstractions.ContractBudgets budgets);
}
