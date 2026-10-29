namespace Frontier.Platform.Contracts;

/// <summary>
/// Why a payload failed its contract. Bounded and secret-free: the message quotes the contract,
/// never the payload, and the location is capped in length.
/// </summary>
/// <param name="Location">The RFC 6901 pointer into the payload, or empty for the payload as a whole.</param>
/// <param name="Keyword">The schema keyword or <c>rule</c> that failed.</param>
/// <param name="Message">What was expected.</param>
public sealed record ContractViolation(string Location, string Keyword, string Message);
