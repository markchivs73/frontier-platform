namespace Frontier.Platform.Contracts;

/// <summary>
/// One registration finding against a contract.
/// </summary>
/// <param name="Code">A stable identifier, such as <c>contract.property-description-missing</c>; see <see cref="ContractFindingCodes"/>.</param>
/// <param name="Severity">Whether it refuses the contract.</param>
/// <param name="Location">An RFC 6901 pointer into the submitted contract (<c>/schema/properties/total</c>, <c>/rules/0</c>), or empty for the contract as a whole.</param>
/// <param name="Message">What is wrong and what to do, for the person registering it.</param>
public sealed record ContractFinding(string Code, ContractFindingSeverity Severity, string Location, string Message);
