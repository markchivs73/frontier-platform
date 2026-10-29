using System.Globalization;
using System.Text.RegularExpressions;

namespace Frontier.Platform.Contracts;

/// <summary>
/// A parsed ADR-E2 contract ref, <c>{namespace}/{name}/{major}.{minor}</c>. <see cref="Id"/> is
/// everything before the version. Edges match on <see cref="Id"/> and <see cref="Major"/> (ADR-E2 D3).
/// </summary>
/// <param name="Id">The contract id, <c>{namespace}/{name}</c>.</param>
/// <param name="Major">The major version.</param>
/// <param name="Minor">The minor version.</param>
public sealed partial record ContractRef(string Id, int Major, int Minor)
{
    /// <summary>Parses <paramref name="value"/>, or returns <see langword="false"/> when it is not a well-formed ref.</summary>
    public static bool TryParse(string? value, out ContractRef? contractRef)
    {
        var match = value is null ? Match.Empty : RefPattern().Match(value);
        contractRef = match.Success
            ? new ContractRef(
                match.Groups["id"].Value,
                int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture))
            : null;
        return match.Success;
    }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Id}/{Major}.{Minor}");

    // The grammar of the compiler's data.schema-ref-match rule, with the version digits bounded so
    // they always fit an int.
    [GeneratedRegex(@"^(?<id>[a-z0-9][a-z0-9\-.]*/[a-z0-9][a-z0-9\-]*)/(?<major>[0-9]{1,9})\.(?<minor>[0-9]{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex RefPattern();
}
