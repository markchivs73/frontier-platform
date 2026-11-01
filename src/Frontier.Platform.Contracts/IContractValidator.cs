using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The one validator (ADR-E27 D8): checks a payload against a pinned contract's schema and rules.
/// A failure is a permanent contract violation; the caller decides how to raise it. Thread-safe;
/// each contract is compiled once, by hash, and its hash is verified when it is. A call that passes a
/// <see cref="ContractBudgets"/> validates under it (a run's pinned record, ADR-PA43); one that does
/// not uses <see cref="ContractsOptions"/>.
/// </summary>
public interface IContractValidator
{
    /// <summary>Validates the UTF-8 JSON in <paramref name="utf8Payload"/>, parsed with <see cref="ContractsOptions.MaxPayloadDepth"/>.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload);

    /// <summary>Validates an already-parsed payload; its depth is checked against <see cref="ContractsOptions.MaxPayloadDepth"/>.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload);

    /// <summary>Validates the UTF-8 JSON in <paramref name="utf8Payload"/> under <paramref name="budgets"/>, which a run pinned at its start.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content, or <paramref name="budgets"/> is out of range.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload, ContractBudgets budgets);

    /// <summary>Validates an already-parsed payload under <paramref name="budgets"/>, which a run pinned at its start.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content, or <paramref name="budgets"/> is out of range.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload, ContractBudgets budgets);
}
