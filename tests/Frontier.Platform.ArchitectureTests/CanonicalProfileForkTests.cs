using System.Text.RegularExpressions;
using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// K10, hard invariant 1: one shared <c>JsonSerializerOptions</c> profile, everywhere.
/// <para>
/// Definition hashing, cache hits and audit signing all compare bytes, so a second options
/// instance does not produce an error — it produces a different hash. A fork is discovered as a
/// cache that stopped hitting or a signature that stopped verifying, a long way from its cause.
/// </para>
/// </summary>
public sealed partial class CanonicalProfileForkTests
{
    /// <summary>
    /// The single legitimate construction site. An allowlist of one, named rather than counted, so
    /// that moving the profile is a deliberate edit here and adding a second is a failure.
    /// </summary>
    private const string ProfileHome = "Frontier.Platform.Serialization";

    [GeneratedRegex(@"new\s+JsonSerializerOptions", RegexOptions.None, 2000)]
    private static partial Regex ConstructsOptions();

    /// <summary>Serialize/Deserialize calls, captured with their arguments so the profile can be looked for.</summary>
    [GeneratedRegex(@"JsonSerializer\.(?:Serialize|Deserialize|SerializeToUtf8Bytes|SerializeToElement|SerializeToNode)[^(]*\(([^;]*)", RegexOptions.None, 2000)]
    private static partial Regex CallsSerializer();

    [Fact]
    public void OnlySerializationConstructsTheOptionsProfile()
    {
        var forks = ScanForks(SourceTree.ProductionFiles());

        Assert.True(forks.Count == 0,
            $"Only {ProfileHome} may construct JsonSerializerOptions (K10, invariant 1). A second profile changes "
            + $"hashes, cache keys and signatures silently.{Environment.NewLine}{string.Join(Environment.NewLine, forks)}");
    }

    [Fact]
    public void EverySerializerCallPassesTheCanonicalProfile()
    {
        var bare = ScanBareCalls(SourceTree.ProductionFiles());

        Assert.True(bare.Count == 0,
            "Every JsonSerializer call must pass CanonicalProfile.Options (K10, invariant 1). Default options "
            + $"reorder properties, keep nulls and rename members.{Environment.NewLine}{string.Join(Environment.NewLine, bare)}");
    }

    /// <summary>
    /// The rules must be able to fire — the S13.23 lesson, that a check certifying nothing looks
    /// exactly like a check finding nothing. Both patterns are exercised against known-bad text,
    /// including the wrapped argument list that this rule's first run misread as a violation.
    /// </summary>
    [Fact]
    public void BothRulesRejectKnownBadSource()
    {
        Assert.Matches(ConstructsOptions(), "var o = new JsonSerializerOptions { WriteIndented = true };");
        Assert.True(IsBareCall("JsonSerializer.Serialize(value);"));
        Assert.False(IsBareCall("JsonSerializer.Serialize(value, CanonicalProfile.Options);"));
        Assert.False(IsBareCall("JsonSerializer.Deserialize<T>(\n    json,\n    CanonicalProfile.Options);"));
    }

    /// <summary>
    /// Both rules are file scans, so an empty or misdirected scan passes them silently. The tree is
    /// pinned by a file that must be in it and a floor on what was read.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsTheSourceTree()
    {
        var files = SourceTree.ProductionFiles();

        Assert.Contains(files, path => path.EndsWith("CanonicalProfile.cs", StringComparison.Ordinal));
        Assert.True(files.Count > 100, $"only {files.Count} source files were scanned — the tree was not found");
    }

    internal static List<string> ScanForks(IEnumerable<string> files) =>
        [.. Findings(files, (line, path) => ConstructsOptions().IsMatch(line) && !IsProfileHome(path))];

    /// <summary>
    /// Scans whole file text, not lines: an argument list is routinely wrapped, and a line-based
    /// reading of one reports a correct call as a violation. Found by this rule's own first run.
    /// </summary>
    internal static List<string> ScanBareCalls(IEnumerable<string> files) =>
        [.. files.Where(path => !IsProfileHome(path)).SelectMany(BareCallsIn)];

    internal static IEnumerable<string> BareCallsIn(string path)
    {
        var text = File.ReadAllText(path);
        foreach (var match in CallsSerializer().Matches(text).Where(m => !MentionsProfile(m.Groups[1].Value)))
        {
            yield return $"{SourceTree.Locate(path, LineOf(text, match.Index))}  {Excerpt(match.Value)}";
        }
    }

    internal static int LineOf(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

    internal static string Excerpt(string matched) =>
        string.Join(' ', matched.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));

    internal static IEnumerable<string> Findings(IEnumerable<string> files, Func<string, string, bool> isViolation)
    {
        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (isViolation(lines[i], path))
                {
                    yield return $"{SourceTree.Locate(path, i + 1)}  {lines[i].Trim()}";
                }
            }
        }
    }

    /// <summary>
    /// A serializer call whose visible arguments never mention the canonical profile.
    /// <para>
    /// A passed-through <c>options</c> parameter is tolerated — <c>ContractMigrator</c> and the
    /// migrating converters take the live options as an argument and must honour them, so demanding
    /// the literal <c>CanonicalProfile.Options</c> there would be wrong. The tolerance is the rule's
    /// known limit: it catches the accidental fork, not a deliberately wrong options instance
    /// threaded through a parameter.
    /// </para>
    /// </summary>
    internal static bool IsBareCall(string text)
    {
        var match = CallsSerializer().Match(text);
        return match.Success && !MentionsProfile(match.Groups[1].Value);
    }

    internal static bool MentionsProfile(string arguments) =>
        arguments.Contains("CanonicalProfile", StringComparison.Ordinal)
        || arguments.Contains("options", StringComparison.OrdinalIgnoreCase);

    internal static bool IsProfileHome(string text) => text.Contains(ProfileHome, StringComparison.Ordinal);
}
