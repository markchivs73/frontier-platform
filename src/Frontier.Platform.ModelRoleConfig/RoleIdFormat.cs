using System.Text.RegularExpressions;

namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// The role id rule (ADR-PA40, S4-13): lowercase kebab-case, at most 64 characters. Kebab-case has
/// no <c>:</c>, the separator in every <c>model-role-config</c> document id, so a role id can never
/// make one role's document ids collide with another's.
/// </summary>
internal static partial class RoleIdFormat
{
    /// <summary>The longest role id accepted.</summary>
    internal const int MaxLength = 64;

    /// <summary>Whether <paramref name="roleId"/> is a well-formed role id.</summary>
    internal static bool IsValid(string? roleId) =>
        roleId is { Length: > 0 and <= MaxLength } && KebabCase().IsMatch(roleId);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KebabCase();
}
