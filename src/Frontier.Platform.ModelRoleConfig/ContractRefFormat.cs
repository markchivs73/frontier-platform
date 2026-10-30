using System.Text.RegularExpressions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The ADR-E2 schema ref grammar, <c>{namespace}/{name}/{major}.{minor}</c>, that a role
/// definition's contract refs must follow (ADR-PA40). The same pattern the compiler's
/// <c>DataSchemaRefMatchRule</c> parses; restated here because this governance-tier library may not
/// reference the compiler (ADR-PA5).
/// </summary>
internal static partial class ContractRefFormat
{
    /// <summary>Whether <paramref name="contractRef"/> is a well-formed ADR-E2 schema ref.</summary>
    internal static bool IsValid(string contractRef) => SchemaRef().IsMatch(contractRef);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-.]*/[a-z0-9][a-z0-9\-]*/\d+\.\d+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SchemaRef();
}
