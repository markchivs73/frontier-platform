namespace Frontier.Platform.Contracts;

/// <summary>One failed result as the schema engine reports it.</summary>
/// <param name="DocumentLocation">The pointer into the payload.</param>
/// <param name="SchemaLocation">The evaluation path through the schema, ending at the keyword that failed. It follows <c>$ref</c>s, so a deeper failure always extends the path of the one that wraps it.</param>
/// <param name="Message">The engine's message; it may be empty or a generic "did not match".</param>
internal sealed record SchemaFailure(string DocumentLocation, string SchemaLocation, string Message);

/// <summary>
/// Turns a schema engine's failed results into bounded, secret-free violations (ADR-E27 D3: the
/// validator's basic output). Only the deepest failures are kept: a failure whose schema location
/// another failure extends is the wrapper around it, not the reason.
/// </summary>
internal static class SchemaViolations
{
    /// <summary>The longest location or message kept; longer text is cut.</summary>
    internal const int MaxTextLength = 200;

    private const string GenericMessage = "The value was expected to match the subschema.";

    /// <summary>The violation for a <c>pattern</c> that ran past its time limit: permanent, like any other.</summary>
    internal static readonly ContractViolation PatternTimeout =
        new(string.Empty, "pattern", "A pattern check ran past its time limit, so the payload is refused.");

    /// <summary>The deepest of <paramref name="failures"/>, de-duplicated, at most <paramref name="maxViolations"/>.</summary>
    internal static IReadOnlyList<ContractViolation> From(IReadOnlyList<SchemaFailure> failures, int maxViolations) =>
        failures
            .Where(failure => !failures.Any(other => Extends(other.SchemaLocation, failure.SchemaLocation)))
            .Select(ToViolation)
            .Distinct()
            .Take(maxViolations)
            .ToList();

    /// <summary>Whether <paramref name="deeper"/> is a strictly deeper schema location than <paramref name="location"/>.</summary>
    internal static bool Extends(string deeper, string location) =>
        deeper.Length > location.Length && deeper.StartsWith(location + "/", StringComparison.Ordinal);

    /// <summary>Cuts <paramref name="text"/> to <see cref="MaxTextLength"/>.</summary>
    internal static string Bound(string text) =>
        text.Length <= MaxTextLength ? text : string.Concat(text.AsSpan(0, MaxTextLength - 1), "…");

    private static ContractViolation ToViolation(SchemaFailure failure)
    {
        var keyword = Keyword(failure.SchemaLocation);
        var message = string.IsNullOrEmpty(failure.Message) || failure.Message == GenericMessage
            ? $"The value failed '{keyword}'."
            : failure.Message;
        return new ContractViolation(Bound(failure.DocumentLocation), keyword, Bound(message));
    }

    private static string Keyword(string schemaLocation)
    {
        var last = schemaLocation[(schemaLocation.LastIndexOf('/') + 1)..];
        return last.Length == 0 ? "schema" : last.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }
}
