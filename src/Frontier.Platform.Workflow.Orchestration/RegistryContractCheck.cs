using System.Text;

using Frontier.Platform.Abstractions;
using Frontier.Platform.Contracts;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Validates a payload against a pinned registry contract (ADR-E27 D8). A payload that fails is a
/// permanent <see cref="ContractViolationException"/> naming the contract, with the validator's
/// bounded, secret-free violations.
/// </summary>
internal static class RegistryContractCheck
{
    /// <summary>Validates <paramref name="payload"/> against <paramref name="contract"/> and returns its UTF-8 bytes.</summary>
    /// <exception cref="ContractViolationException">The payload does not satisfy the contract.</exception>
    internal static byte[] Ensure(IContractValidator validator, ContractSnapshot contract, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var result = validator.Validate(contract, bytes);
        return result.IsValid
            ? bytes
            : throw new ContractViolationException(contract.ContractRef, [.. result.Violations.Select(Describe)]);
    }

    /// <summary>One violation as text: where, which keyword, and the validator's message.</summary>
    internal static string Describe(ContractViolation violation) =>
        $"{(violation.Location.Length == 0 ? "/" : violation.Location)}: {violation.Keyword}: {violation.Message}";
}
