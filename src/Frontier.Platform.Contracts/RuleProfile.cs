using Celly.Ast;

namespace Frontier.Platform.Contracts;

/// <summary>
/// The CEL profile (ADR-E27 D2): the standard library, without <c>matches()</c> and without named
/// time zones. A pattern belongs in the schema's <c>pattern</c> keyword, which runs under the
/// validator's timeout; a time zone makes a rule's result depend on tz data that changes.
/// Map-order independence needs no check here: every map a rule sees is built in key order.
/// </summary>
internal static class RuleProfile
{
    /// <summary>The timestamp accessors that take an optional time-zone argument.</summary>
    internal static readonly IReadOnlySet<string> TimeZoneAccessors = new HashSet<string>(StringComparer.Ordinal)
    {
        "getFullYear", "getMonth", "getDayOfYear", "getDayOfMonth", "getDate", "getDayOfWeek",
        "getHours", "getMinutes", "getSeconds", "getMilliseconds",
    };

    /// <summary>Why <paramref name="expression"/> leaves the profile, one reason per offending call.</summary>
    internal static IEnumerable<string> Violations(Expr expression) =>
        AstTools.DescendantsAndSelf(expression).OfType<CallExpr>().Select(Violation).OfType<string>();

    private static string? Violation(CallExpr call) => call switch
    {
        { Function: "matches" } => "matches() is not allowed in a contract rule; put the pattern in the schema's \"pattern\" keyword instead.",
        { Target: not null, Args.Count: 1 } when TimeZoneAccessors.Contains(call.Function) =>
            $"{call.Function}() with a time zone is not allowed in a contract rule; timestamps are compared in UTC.",
        _ => null,
    };
}
