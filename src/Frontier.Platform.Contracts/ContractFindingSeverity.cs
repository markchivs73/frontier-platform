using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Contracts;

/// <summary>Whether a registration finding refuses the contract or only reports on it.</summary>
public sealed class ContractFindingSeverity : SmartEnum<ContractFindingSeverity>
{
    /// <summary>The contract is refused.</summary>
    public static readonly ContractFindingSeverity Error = new("error");

    /// <summary>The contract is accepted, and the finding is shown on its record.</summary>
    public static readonly ContractFindingSeverity Warning = new("warning");

    private ContractFindingSeverity(string name)
        : base(name)
    {
    }
}
