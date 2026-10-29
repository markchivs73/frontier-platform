namespace Frontier.Platform.Contracts;

/// <summary>The outcome of validating one payload against one contract.</summary>
/// <param name="Violations">Why it failed, at most <see cref="ContractsOptions.MaxViolations"/>; empty when it passed.</param>
public sealed record ContractValidationResult(IReadOnlyList<ContractViolation> Violations)
{
    /// <summary>A passing result.</summary>
    public static ContractValidationResult Valid { get; } = new([]);

    /// <summary>Whether the payload satisfies the contract.</summary>
    public bool IsValid => Violations.Count == 0;
}
