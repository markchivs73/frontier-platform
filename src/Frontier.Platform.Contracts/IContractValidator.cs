using System.Text.Json;

using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The one validator (ADR-E27 D8): checks a payload against a pinned contract's schema and rules.
/// A failure is a permanent contract violation; the caller decides how to raise it. Thread-safe;
/// each contract is compiled once, by hash, and its hash is verified when it is.
/// </summary>
public interface IContractValidator
{
    /// <summary>Validates the UTF-8 JSON in <paramref name="utf8Payload"/>, parsed with <see cref="ContractsOptions.MaxPayloadDepth"/>.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, ReadOnlyMemory<byte> utf8Payload);

    /// <summary>Validates an already-parsed payload; its depth is checked against <see cref="ContractsOptions.MaxPayloadDepth"/>.</summary>
    /// <exception cref="ContractViolationException">The snapshot's hash does not match its content.</exception>
    ContractValidationResult Validate(ContractSnapshot contract, JsonElement payload);
}
