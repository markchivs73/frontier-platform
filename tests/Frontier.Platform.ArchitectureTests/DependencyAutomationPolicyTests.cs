using System.Text.RegularExpressions;
using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// Dependabot and CodeQL run on Mark's terms (2026-11-13; frontier-workflow S13.254). Both are
/// weekly, because every Actions minute is paid from a hobby budget, so CodeQL never runs per pull
/// request and Dependabot keeps its open-PR limits small. Dependabot never bumps the platform's
/// own packages, skips the <c>samples/</c> smoke consumer, and titles its PRs with the
/// <c>deps</c> commit-lint scope. This mirrors the consumer repository's
/// <c>DependencyAutomationPolicyTests</c> and keeps <c>.github/dependabot.yml</c> and
/// <c>.github/workflows/codeql.yml</c> on those decisions.
/// </summary>
public sealed partial class DependencyAutomationPolicyTests
{
    [GeneratedRegex(@"^  - package-ecosystem:\s*(?<value>\S+)\s*$", RegexOptions.Multiline, 2000)]
    private static partial Regex Ecosystem();

    [GeneratedRegex(@"^\s*interval:\s*(?<value>\S+)\s*$", RegexOptions.Multiline, 2000)]
    private static partial Regex Interval();

    [GeneratedRegex(@"^\s*open-pull-requests-limit:\s*(?<value>\d+)\s*$", RegexOptions.Multiline, 2000)]
    private static partial Regex PullRequestLimit();

    [GeneratedRegex(@"^\s*prefix:\s*""(?<value>[^""]*)""\s*$", RegexOptions.Multiline, 2000)]
    private static partial Regex CommitPrefix();

    [GeneratedRegex(@"^on:\s*$(?<block>(\r?\n([ \t].*)?)*)", RegexOptions.Multiline, 2000)]
    private static partial Regex TriggerBlock();

    [GeneratedRegex(@"^  (?<value>[a-z_]+):", RegexOptions.Multiline, 2000)]
    private static partial Regex Trigger();

    [GeneratedRegex(@"uses:\s*(?<value>actions/[a-z-]+@\S+)", RegexOptions.None, 2000)]
    private static partial Regex FirstPartyAction();

    [Fact]
    public void Dependabot_CoversNuGetAndGitHubActions_IncludingTheCompositeAction()
    {
        var entries = Entries();

        Assert.Equal(["github-actions", "nuget"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(@"- ""/.github/actions/build-and-test""", entries["github-actions"], StringComparison.Ordinal);
    }

    [Fact]
    public void Dependabot_UpdatesEveryEcosystemWeeklyOnMondayAtSixUtc() =>
        Assert.All(Entries().Values, entry =>
        {
            Assert.Equal(["weekly"], Values(Interval(), entry));
            Assert.Contains("day: monday", entry, StringComparison.Ordinal);
            Assert.Contains(@"time: ""06:00""", entry, StringComparison.Ordinal);
            Assert.Contains("timezone: Etc/UTC", entry, StringComparison.Ordinal);
        });

    [Fact]
    public void Dependabot_KeepsEveryOpenPullRequestLimitSmall()
    {
        var entries = Entries();

        Assert.Equal(["3"], Values(PullRequestLimit(), entries["nuget"]));
        Assert.Equal(["2"], Values(PullRequestLimit(), entries["github-actions"]));
    }

    [Fact]
    public void Dependabot_GroupsMinorAndPatchApartFromMajor_AndEveryActionTogether()
    {
        var entries = Entries();

        Assert.Contains("update-types: [minor, patch]", entries["nuget"], StringComparison.Ordinal);
        Assert.Contains("update-types: [major]", entries["nuget"], StringComparison.Ordinal);
        Assert.Contains(@"patterns: [""*""]", entries["github-actions"], StringComparison.Ordinal);
    }

    [Fact]
    public void Dependabot_NeverBumpsThePlatformPackages_OrTheSamplesConsumer()
    {
        var nuget = Entries()["nuget"];

        Assert.Contains(@"- dependency-name: ""Frontier.Platform.*""", nuget, StringComparison.Ordinal);
        Assert.Contains(@"- ""samples/**""", nuget, StringComparison.Ordinal);
    }

    [Fact]
    public void Dependabot_TitlesEveryPullRequestWithTheDepsScope() =>
        Assert.All(Entries().Values, entry => Assert.Equal(["deps"], Values(CommitPrefix(), entry)));

    [Fact]
    public void CodeQl_RunsWeeklyAndOnDemand_NeverPerPullRequestOrPush()
    {
        var block = TriggerBlock().Match(CodeQl()).Groups["block"].Value;

        Assert.Equal(["schedule", "workflow_dispatch"], Values(Trigger(), block).Order(StringComparer.Ordinal));
        Assert.Contains(@"- cron: ""0 5 * * 1""", block, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeQl_ReadsContentsByDefault_AndOnlyTheJobWritesSecurityEvents()
    {
        var codeQl = CodeQl();
        var jobs = codeQl.IndexOf("\njobs:", StringComparison.Ordinal);

        Assert.Contains("permissions:\n  contents: read\n", codeQl[..jobs], StringComparison.Ordinal);
        Assert.DoesNotContain("write", codeQl[..jobs], StringComparison.Ordinal);
        Assert.Contains("security-events: write", codeQl[jobs..], StringComparison.Ordinal);
        Assert.Contains("actions: read", codeQl[jobs..], StringComparison.Ordinal);
    }

    [Fact]
    public void CodeQl_PinsTheSameActionMajorsAsCi()
    {
        var ci = Values(FirstPartyAction(), Read(".github", "workflows", "ci.yml"));

        Assert.All(Values(FirstPartyAction(), CodeQl()), action => Assert.Contains(action, ci));
        Assert.Equal(2, Values(FirstPartyAction(), CodeQl()).Count);
        Assert.Contains("github/codeql-action/analyze@v4", CodeQl(), StringComparison.Ordinal);
    }

    [Fact]
    public void CodeQl_TracesTheShippedReleaseBuildOnTheGlobalJsonSdk()
    {
        var codeQl = CodeQl();

        Assert.Contains("fetch-depth: 0", codeQl, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", codeQl, StringComparison.Ordinal);
        Assert.Contains("build-mode: manual", codeQl, StringComparison.Ordinal);
        Assert.Contains("languages: csharp", codeQl, StringComparison.Ordinal);
        Assert.Contains("run: dotnet build FrontierPlatform.slnx -c Release", codeQl, StringComparison.Ordinal);
    }

    private static string Dependabot() => Read(".github", "dependabot.yml");

    private static string CodeQl() => Read(".github", "workflows", "codeql.yml");

    private static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([SourceTree.RepositoryRoot(), .. relativePath])).ReplaceLineEndings("\n");

    /// <summary>Each <c>updates</c> entry's text, keyed by its ecosystem.</summary>
    private static Dictionary<string, string> Entries()
    {
        var dependabot = Dependabot();
        var starts = Ecosystem().Matches(dependabot).ToList();

        return starts.Select((m, i) => (m.Groups["value"].Value, Text: dependabot[m.Index..(i + 1 < starts.Count ? starts[i + 1].Index : dependabot.Length)]))
            .ToDictionary(e => e.Value, e => e.Text, StringComparer.Ordinal);
    }

    private static List<string> Values(Regex pattern, string text) =>
        [.. pattern.Matches(text).Select(m => m.Groups["value"].Value)];
}
