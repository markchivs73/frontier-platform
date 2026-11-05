using System.Text.RegularExpressions;
using System.Xml.Linq;
using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// K7, invariant 7: retry policy is data — a <c>RetryPolicySpec</c> resolved to a named profile by
/// <c>Frontier.Platform.Resilience</c> — never a literal retry count or backoff in code. The
/// cheapest way to write one is to reach for Polly directly, so only Resilience may take a Polly
/// package reference, and only Resilience's own files (plus the allowlist below) may name Polly in
/// code. This mirrors the consumer repository's <c>PollyBoundaryTests</c> (S13.211); this is the
/// platform half (S13.234).
/// </summary>
public sealed class PollyBoundaryTests
{
    /// <summary>The one build file that may reference Polly: the library that owns retry policy.</summary>
    private const string OwnerProject = "src/Frontier.Platform.Resilience/Frontier.Platform.Resilience.csproj";

    /// <summary>The owner's source directory: every file under it may name Polly types.</summary>
    private const string OwnerSourceDirectory = "src/Frontier.Platform.Resilience/";

    /// <summary>
    /// Production files outside the owner that may name Polly in code, keyed by repo-relative path
    /// (forward slashes) so an entry exempts exactly one file, each with the reason it writes no
    /// policy. Empty today: <c>AgentInvocationDispatcher</c> executes the pipeline Resilience hands
    /// it through <c>var</c>, and the other mentions are prose in comments, which the rule ignores.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal);

    /// <summary>No build file but the owner's takes a Polly package reference.</summary>
    [Fact]
    public void OnlyResilienceReferencesAPollyPackage()
    {
        var offenders = BuildFiles()
            .Where(path => RelativePath(path) != OwnerProject)
            .Where(path => PollyBoundary.ReferencesPolly(XDocument.Load(path)))
            .Select(RelativePath)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Polly arrives only through Frontier.Platform.Resilience (K7): retry policy is a "
            + "RetryPolicySpec, not code. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>Anti-vacuity: the owner still references Polly, so the exemption still means something.</summary>
    [Fact]
    public void TheOwnerStillReferencesPolly() =>
        Assert.True(PollyBoundary.ReferencesPolly(XDocument.Load(Absolute(OwnerProject))),
            $"{OwnerProject} no longer references Polly — move or remove the owner exemption.");

    /// <summary>No production file outside the owner and the allowlist names the Polly namespace in code.</summary>
    [Fact]
    public void OnlyResilienceAndAllowlistedFilesNamePollyTypes()
    {
        var offenders = SourceTree.ProductionFiles()
            .Select(RelativePath)
            .Where(path => !path.StartsWith(OwnerSourceDirectory, StringComparison.Ordinal))
            .Where(path => !Allowed.ContainsKey(path))
            .Where(path => PollyBoundary.UsesPolly(File.ReadAllText(Absolute(path))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Polly types may appear only in Frontier.Platform.Resilience and the allowlisted files (K7) "
            + "— express retry as a RetryPolicySpec. Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>Anti-vacuity (S13.37): an allowlist entry must still exist and still trip the rule.</summary>
    [Fact]
    public void EveryAllowlistedFileStillExistsAndStillNamesPolly()
    {
        foreach (var (path, reason) in Allowed)
        {
            Assert.True(File.Exists(Absolute(path)), $"{path} is allowlisted ({reason}) but no longer exists — remove the entry.");
            Assert.True(PollyBoundary.UsesPolly(File.ReadAllText(Absolute(path))),
                $"{path} is allowlisted ({reason}) but no longer names Polly — remove the entry.");
        }
    }

    /// <summary>
    /// Anti-vacuity: both walks find the real tree — the owner's files, the central package file and
    /// the solution's projects — so neither rule can pass by scanning nothing.
    /// </summary>
    [Fact]
    public void BothScansActuallyReadTheRepository()
    {
        var buildFiles = BuildFiles().Select(RelativePath).ToList();
        var sources = SourceTree.ProductionFiles().Select(RelativePath).ToList();

        Assert.Contains(OwnerProject, buildFiles);
        Assert.Contains("tests/Frontier.Platform.ArchitectureTests/Frontier.Platform.ArchitectureTests.csproj", buildFiles);
        Assert.Contains("Directory.Packages.props", buildFiles);
        Assert.True(buildFiles.Count > 10, $"only {buildFiles.Count} build files were scanned — the tree was not found");
        Assert.Contains(sources, path => path.StartsWith(OwnerSourceDirectory, StringComparison.Ordinal)
            && PollyBoundary.UsesPolly(File.ReadAllText(Absolute(path))));
    }

    /// <summary>Each reference shape the rule must reject fires the predicate, including case variants and item lists.</summary>
    [Theory]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Polly\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"polly.core\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"POLLY\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Foo;Polly\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\" Foo ; Polly.Core \" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Polly.Core\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Update=\"Polly.Extensions\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><GlobalPackageReference Include=\"Polly.RateLimiting\" /></ItemGroup></Project>")]
    public void APlantedPollyPackageReferenceIsCaught(string project) =>
        Assert.True(PollyBoundary.ReferencesPolly(XDocument.Parse(project)));

    /// <summary>A version pin, an unrelated package, a near-miss name and an id-less item do not fire.</summary>
    [Theory]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Foo;PollyFill\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageVersion Include=\"Polly.Core\" Version=\"8.7.0\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Frontier.Platform.Resilience\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"PollyFill\" /></ItemGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference /></ItemGroup></Project>")]
    public void AVersionPinOrUnrelatedPackageIsNotAReference(string project) =>
        Assert.False(PollyBoundary.ReferencesPolly(XDocument.Parse(project)));

    /// <summary>Planted uses of the Polly namespace in code fire the predicate, including after a comment line.</summary>
    [Theory]
    [InlineData("using Polly;")]
    [InlineData("using Polly.Retry;")]
    [InlineData("var pipeline = new Polly.ResiliencePipelineBuilder();")]
    [InlineData("/// <summary>Runs the call.</summary>\nusing Polly.CircuitBreaker;")]
    [InlineData("var builder = new Polly.ResiliencePipelineBuilder(); // retry three times")]
    public void APlantedPollyUseInSourceIsCaught(string source) =>
        Assert.True(PollyBoundary.UsesPolly(source));

    /// <summary>Source that names Polly only in prose comments, or not at all, including a near-miss identifier, does not fire.</summary>
    [Theory]
    [InlineData("using Frontier.Platform.Resilience;")]
    [InlineData("var policy = new RetryPolicySpec();")]
    [InlineData("var polyline = PollyFill.Create();")]
    [InlineData("/// resolves to a named Polly profile\nvar policy = new RetryPolicySpec();")]
    [InlineData("    // through the inner Polly pipeline\r\nvar x = 1;")]
    public void SourceWithoutPollyIsNotCaught(string source) =>
        Assert.False(PollyBoundary.UsesPolly(source));

    private static readonly string[] BuildFilePatterns = ["*.csproj", "*.props", "*.targets"];

    /// <summary>Every <c>.csproj</c>, <c>.props</c> and <c>.targets</c> file in the repository, excluding build output.</summary>
    private static IEnumerable<string> BuildFiles() =>
        BuildFilePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(SourceTree.RepositoryRoot(), pattern, SearchOption.AllDirectories))
            .Where(SourceTree.IsProductionFile);

    /// <summary>The repo-relative, forward-slash form of an absolute path, as the allowlist keys it.</summary>
    private static string RelativePath(string path) =>
        Path.GetRelativePath(SourceTree.RepositoryRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The absolute path of a repo-relative, forward-slash path.</summary>
    private static string Absolute(string relativePath) =>
        Path.Combine(SourceTree.RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>The S13.234 predicates, kept apart from the file walk so a planted violation can prove each one fires.</summary>
internal static partial class PollyBoundary
{
    private static readonly string[] ReferenceElements = ["PackageReference", "GlobalPackageReference"];

    /// <summary>
    /// True when the build file takes a package reference to Polly or any <c>Polly.*</c> package.
    /// NuGet ids are case-insensitive and an item's <c>Include</c> may be a <c>;</c>-separated list,
    /// so every listed id is compared ignoring case.
    /// </summary>
    internal static bool ReferencesPolly(XDocument project) =>
        project.Descendants()
            .Where(element => ReferenceElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(element => (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"))
            .SelectMany(ids => (ids ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Any(IsPollyPackage);

    /// <summary>
    /// True when the source text names the Polly namespace in code. Whole-line <c>//</c> and
    /// <c>///</c> comments are dropped first: the platform's doc comments describe "the inner Polly
    /// pipeline" in prose, and exempting those files outright would also exempt a real use in them.
    /// </summary>
    internal static bool UsesPolly(string source) =>
        PollyNamespace().IsMatch(CommentLine().Replace(source, string.Empty));

    private static bool IsPollyPackage(string id) =>
        id.Equals("Polly", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("Polly.", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\bPolly\b", RegexOptions.None, 2000)]
    private static partial Regex PollyNamespace();

    [GeneratedRegex(@"^[ \t]*//.*$", RegexOptions.Multiline, 2000)]
    private static partial Regex CommentLine();
}
