using System.Text.RegularExpressions;

namespace Frontier.Platform.Workflow.Compiler.Rules;

/// <summary>
/// The tag grammar <c>structure.required-fields</c> checks (S13.238, triage 2026-11-24): kebab-case, read strictly
/// as lowercase letters and digits in groups joined by single hyphens and nothing else (<c>\z</c>, so a
/// trailing newline is not accepted), at most <see cref="MaxLength"/> characters,
/// and at most <see cref="MaxCount"/> tags on a definition.
/// </summary>
internal static partial class DefinitionTagFormat
{
    /// <summary>The longest tag allowed.</summary>
    internal const int MaxLength = 32;

    /// <summary>The most tags a definition may carry.</summary>
    internal const int MaxCount = 10;

    /// <summary>Why <paramref name="tag"/> is not a valid tag, or null when it is. Length is checked first, so the pattern never runs on a long input.</summary>
    internal static string? Problem(string? tag) =>
        tag is null || tag.Length == 0 ? "is empty"
        : tag.Length > MaxLength ? $"is longer than {MaxLength} characters"
        : !KebabCase().IsMatch(tag) ? "is not kebab-case (lowercase letters and digits, single hyphens between them)"
        : null;

    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KebabCase();
}
