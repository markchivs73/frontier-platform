using System.Reflection;
using System.Text.RegularExpressions;
using Frontier.Platform.ArchitectureTests.Determinism;
using Frontier.Platform.Workflow.Compiler.Rules;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// S13.235 (K7, doc 08 §2): models are named by role and resolved through Model-Role config, so no
/// model id is written in code. <c>model-role.no-model-ids</c> (<c>ModelIdRejectionRule</c>) guards
/// definitions; this applies the same rule's pattern to every string literal in this repository's
/// <c>src/**/*.cs</c> outside Model-Role config itself (<c>Frontier.Platform.ModelRoleConfig</c>,
/// whose frozen Phase 1 mapping is where ids belong). Comments are not scanned — a doc comment
/// naming a model it was observed with configures nothing. The other files under <c>src</c> that
/// can carry a value (<c>.razor</c>, <c>.cshtml</c>, <c>.json</c>, <c>.js</c>) have no C# lexer
/// here, so their whole text is scanned, comments included. The platform had none when this rule
/// was written, so that rule's walk and predicate are proven against a planted tree rather than
/// by the real one. This mirrors the consumer repository's <c>ModelIdSourceTests</c> (S13.212).
/// </summary>
public sealed class ModelIdSourceTests
{
    /// <summary>
    /// Production files outside the owner whose string literals may match the pattern, keyed by
    /// repo-relative path (forward slashes) so an entry exempts exactly one file, each with the
    /// reason the match is not a model id in code. Empty today: the other mentions in
    /// <c>ContextAssembly</c>, <c>Abstractions</c> and the rule itself are doc-comment examples.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal);

    /// <summary>No string literal in production source outside the owner and the allowlist matches the rule's model-id pattern.</summary>
    [Fact]
    public void NoProductionStringLiteralNamesAModelId()
    {
        var offenders = Scanned(SourceTree.ProductionFiles())
            .SelectMany(path => ModelIdSourceScan.Offenders(path, ModelIdSourceScan.Find(File.ReadAllText(path))))
            .ToList();

        AssertNone(offenders);
    }

    /// <summary>No value-bearing non-C# file under <c>src</c> outside the owner and the allowlist matches the pattern anywhere in its text.</summary>
    [Fact]
    public void NoProductionMarkupOrConfigFileNamesAModelId()
    {
        var offenders = Scanned(ModelIdSourceScan.ValueBearingFiles(SourceTree.RepositoryRoot()))
            .SelectMany(path => ModelIdSourceScan.Offenders(path, ModelIdSourceScan.FindInText(File.ReadAllText(path))))
            .ToList();

        AssertNone(offenders);
    }

    /// <summary>Anti-vacuity: the owner still writes a model id in a literal, so its exemption still means something.</summary>
    [Fact]
    public void TheOwnerStillNamesAModelIdInALiteral() =>
        Assert.Contains(SourceTree.ProductionFiles(), path => IsOwned(path)
            && ModelIdSourceScan.Find(File.ReadAllText(path)).Count > 0);

    /// <summary>Anti-vacuity (S13.37): an allowlist entry must still exist and still trip the rule.</summary>
    [Fact]
    public void EveryAllowlistedFileStillExistsAndStillMatches() =>
        Assert.Empty(ModelIdSourceScan.AllowlistProblems(Allowed, SourceTree.RepositoryRoot()));

    /// <summary>The allowlist check rejects an entry with no reason, a missing file, or a file that no longer matches, and keeps one that still matches.</summary>
    [Theory]
    [InlineData("src/A/Gone.cs", "a reason", null, "no longer exists")]
    [InlineData("src/A/Plain.cs", " ", "var m = \"gpt-4o\";", "without a reason")]
    [InlineData("src/A/Plain.cs", "a reason", "// gpt-4o\nvar m = \"deep-reasoning\";", "no longer matches")]
    [InlineData("src/A/plain.json", "a reason", "{ \"role\": \"deep-reasoning\" }", "no longer matches")]
    [InlineData("src/A/Plain.cs", "a reason", "var m = \"gpt-4o\";", null)]
    [InlineData("src/A/plain.json", "a reason", "{ \"note\": \"gpt-4o\" }", null)]
    public void TheAllowlistCheckRejectsAStaleEntry(string path, string reason, string? text, string? problem)
    {
        using var tree = new PlantedTree();
        if (text is not null)
        {
            tree.Write(path, text);
        }

        var problems = ModelIdSourceScan.AllowlistProblems(new Dictionary<string, string> { [path] = reason }, tree.Root).ToList();

        if (problem is null)
        {
            Assert.Empty(problems);
        }
        else
        {
            Assert.Contains(problem, Assert.Single(problems), StringComparison.Ordinal);
        }
    }

    /// <summary>An allowlist entry exempts exactly the one file it names: a sibling or a case variant is still scanned.</summary>
    [Fact]
    public void AnAllowlistEntryExemptsExactlyOneFile()
    {
        var root = SourceTree.RepositoryRoot();
        string[] paths = [Path.Combine(root, "src", "A", "x.cs"), Path.Combine(root, "src", "A", "X.cs"), Path.Combine(root, "src", "A", "y.cs")];

        var scanned = ModelIdSourceScan.Scanned(paths, new Dictionary<string, string>(StringComparer.Ordinal) { ["src/A/x.cs"] = "reason" }, root);

        Assert.Equal(paths[1..], scanned);
    }

    /// <summary>The markup-and-config walk finds each value-bearing kind under <c>src</c>, skipping build output and C# source.</summary>
    [Fact]
    public void TheMarkupWalkFindsValueBearingFilesOutsideBuildOutput()
    {
        using var tree = new PlantedTree();
        string[] found = ["src/A/a.razor", "src/A/b.cshtml", "src/A/c.json", "src/A/d.js"];
        foreach (var path in found.Concat(["src/A/bin/e.json", "src/A/obj/f.js", "src/A/g.cs", "src/A/h.txt"]))
        {
            tree.Write(path, "{}");
        }

        var walked = ModelIdSourceScan.ValueBearingFiles(tree.Root).Select(path => ModelIdSourceScan.RelativePath(path, tree.Root));

        Assert.Equal(found, walked.Order(StringComparer.Ordinal));
    }

    /// <summary>Anti-vacuity: the walk reads the real tree and finds its literals, so the rule cannot pass by scanning nothing.</summary>
    [Fact]
    public void TheScanActuallyReadsTheSourceTreesLiterals()
    {
        var files = Scanned(SourceTree.ProductionFiles()).ToList();
        var literals = files.Sum(path => StringLiteralScanner.Scan(File.ReadAllText(path)).Count);

        Assert.True(files.Count > 100, $"only {files.Count} source files were scanned — the tree was not found");
        Assert.True(literals > 1000, $"only {literals} string literals were found — the scanner is not reading the source");
    }

    /// <summary>Planted model ids in each literal form fire the scan.</summary>
    [Theory]
    [InlineData("var model = \"claude-opus-4-8\";", "claude-opus-4-8")]
    [InlineData("var model = @\"gpt-4o\";", "gpt-4o")]
    [InlineData("var model = $\"{prefix}/gemini-2.5-pro\";", "gemini-2.5-pro")]
    [InlineData("var model = $@\"llama-3.1-70b\";", "llama-3.1-70b")]
    [InlineData("var model = \"\"\"\n  mistral-large-2\n  \"\"\";", "mistral-large-2")]
    [InlineData("var model = $\"{Pick(\"deepseek-r1\")}\";", "deepseek-r1")]
    [InlineData("Configure(\"role\", \"Claude-Sonnet-4\");", "Claude-Sonnet-4")]
    public void APlantedModelIdInALiteralIsCaught(string source, string modelId) =>
        Assert.Contains(ModelIdSourceScan.Find(source), hit => hit.Text == modelId);

    /// <summary>A model id in a comment, a role name, an identifier or a near-miss word does not fire.</summary>
    [Theory]
    [InlineData("// observed with claude-opus-4-8\nvar x = 1;")]
    [InlineData("/// <c>claude-opus-4-8</c> rejects temperature\nvar x = 1;")]
    [InlineData("/* gpt-4o */ var x = 1;")]
    [InlineData("var role = \"deep-reasoning\";")]
    [InlineData("var x = gpt - 4;")]
    [InlineData("var word = \"claudette\";")]
    public void TextThatIsNotAModelIdLiteralIsNotCaught(string source) =>
        Assert.Empty(ModelIdSourceScan.Find(source));

    /// <summary>A hit reports the line its literal starts on, so a failure is clickable.</summary>
    [Fact]
    public void AHitReportsItsLiteralsLine() =>
        Assert.Equal(3, Assert.Single(ModelIdSourceScan.Find("var a = 1;\nvar b = 2;\nvar m = \"gpt-4o\";")).Line);

    /// <summary>A planted violation's offender message names its repo-relative <c>path:line</c> and the id, the text the tree rule fails with.</summary>
    [Fact]
    public void APlantedViolationIsReportedByPathAndLine()
    {
        var planted = Path.Combine(SourceTree.RepositoryRoot(), "src", "Planted", "Planted.cs");

        var offender = Assert.Single(ModelIdSourceScan.Offenders(planted,
            ModelIdSourceScan.Find("var a = 1;\nvar b = 2;\nvar m = \"gpt-4o\";")));

        Assert.Equal($"{Path.Combine("src", "Planted", "Planted.cs")}:3 'gpt-4o'", offender);
    }

    /// <summary>The owner exemption covers Model-Role config's files and nothing beside them, including a look-alike prefix.</summary>
    [Theory]
    [InlineData("Frontier.Platform.ModelRoleConfig", true)]
    [InlineData("Frontier.Platform.ModelRoleConfigExtras", false)]
    [InlineData("Frontier.Platform.ContextAssembly", false)]
    public void TheOwnerExemptionCoversOnlyModelRoleConfig(string project, bool owned) =>
        Assert.Equal(owned, IsOwned(Path.Combine(SourceTree.RepositoryRoot(), "src", project, "Planted.cs")));

    /// <summary>A whole-text scan catches an id in markup or config, comments included, on the line it sits.</summary>
    [Theory]
    [InlineData("<p>\n@* gpt-4o *@\n</p>", "gpt-4o", 2)]
    [InlineData("{\n  \"model\": \"claude-opus-4-8\"\n}", "claude-opus-4-8", 2)]
    [InlineData("const m = 'Gemini-2.5-pro';", "Gemini-2.5-pro", 1)]
    public void APlantedModelIdInMarkupOrConfigIsCaught(string text, string modelId, int line) =>
        Assert.Equal(new StringLiteral(modelId, line), Assert.Single(ModelIdSourceScan.FindInText(text)));

    /// <summary>Markup or config naming only roles does not fire.</summary>
    [Fact]
    public void MarkupNamingOnlyRolesIsNotCaught() =>
        Assert.Empty(ModelIdSourceScan.FindInText("{ \"role\": \"deep-reasoning\", \"note\": \"claudette\" }"));

    private static void AssertNone(List<string> offenders) =>
        Assert.True(offenders.Count == 0,
            "A model id in code is policy in code (K7): name a role and let Model-Role config map it "
            + "(doc 08 §2). Offenders: " + string.Join(", ", offenders));

    /// <summary>The real tree's files a rule scans: everything given except the owner's files and the allowlist.</summary>
    private static IEnumerable<string> Scanned(IEnumerable<string> paths) =>
        ModelIdSourceScan.Scanned(paths, Allowed, SourceTree.RepositoryRoot());

    private static bool IsOwned(string path) => ModelIdSourceScan.IsOwned(path, SourceTree.RepositoryRoot());

    /// <summary>A throwaway repository root for planting files; deleted on dispose.</summary>
    private sealed class PlantedTree : IDisposable
    {
        /// <summary>The planted root, standing in for the repository root.</summary>
        internal string Root { get; } = Directory.CreateTempSubdirectory("s13235-").FullName;

        /// <summary>Writes <paramref name="text"/> at the root-relative, forward-slash <paramref name="path"/>.</summary>
        internal void Write(string path, string text)
        {
            var absolute = ModelIdSourceScan.Absolute(path, Root);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, text);
        }

        /// <inheritdoc />
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

/// <summary>The S13.235 predicate, kept apart from the file walk so a planted violation can prove it fires.</summary>
internal static class ModelIdSourceScan
{
    /// <summary>
    /// <c>ModelIdRejectionRule</c>'s private pattern, read by reflection so the source scan and the
    /// definition rule cannot disagree. If the rule renames or removes the field, the type
    /// initializer throws and every test that scans fails with this message.
    /// </summary>
    internal static Regex Pattern { get; } =
        typeof(ModelIdRejectionRule).GetField("ModelIdPattern", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Regex
        ?? throw new InvalidOperationException("ModelIdRejectionRule.ModelIdPattern was not found — update ModelIdSourceScan.");

    /// <summary>Every model-id match inside a string literal of <paramref name="source"/>, with the literal's line.</summary>
    internal static IReadOnlyList<StringLiteral> Find(string source) =>
        [.. StringLiteralScanner.Scan(source)
            .SelectMany(literal => Pattern.Matches(literal.Text).Select(m => new StringLiteral(m.Value, literal.Line)))];

    /// <summary>Every model-id match anywhere in <paramref name="text"/>, with the 1-based line it starts on.</summary>
    internal static IReadOnlyList<StringLiteral> FindInText(string text) =>
        [.. Pattern.Matches(text).Select(m => new StringLiteral(m.Value, 1 + text.AsSpan(0, m.Index).Count('\n')))];

    /// <summary>Model-Role config's source directory: the one place a model id may be written.</summary>
    private const string OwnerSourceDirectory = "src/Frontier.Platform.ModelRoleConfig/";

    /// <summary>The non-C# file kinds under <c>src</c> that can carry a configured value.</summary>
    private static readonly string[] ValueBearingExtensions = [".razor", ".cshtml", ".json", ".js"];

    /// <summary>Every hit in a file's <paramref name="text"/>: literals only for C#, the whole text otherwise.</summary>
    internal static IReadOnlyList<StringLiteral> Hits(string path, string text) =>
        path.EndsWith(".cs", StringComparison.Ordinal) ? Find(text) : FindInText(text);

    /// <summary>What is stale about each allowlist entry under <paramref name="root"/>: no reason, no file, or no longer a match.</summary>
    internal static IEnumerable<string> AllowlistProblems(IReadOnlyDictionary<string, string> allowed, string root) =>
        allowed.Select(entry => AllowlistProblem(entry.Key, entry.Value, root)).OfType<string>();

    /// <summary>What is stale about one allowlist entry, or <see langword="null"/> when it still earns its place.</summary>
    internal static string? AllowlistProblem(string path, string reason, string root)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return $"{path} is allowlisted without a reason.";
        }

        var absolute = Absolute(path, root);
        if (!File.Exists(absolute))
        {
            return $"{path} is allowlisted ({reason}) but no longer exists — remove the entry.";
        }

        return Hits(path, File.ReadAllText(absolute)).Count == 0
            ? $"{path} is allowlisted ({reason}) but no longer matches — remove the entry."
            : null;
    }

    /// <summary>The paths a rule scans: everything given except the owner's files and the allowlisted ones, keyed exactly.</summary>
    internal static IEnumerable<string> Scanned(IEnumerable<string> paths, IReadOnlyDictionary<string, string> allowed, string root) =>
        paths.Where(path => !IsOwned(path, root) && !allowed.ContainsKey(RelativePath(path, root)));

    /// <summary>Whether <paramref name="path"/> sits in Model-Role config's source directory under <paramref name="root"/>.</summary>
    internal static bool IsOwned(string path, string root) =>
        RelativePath(path, root).StartsWith(OwnerSourceDirectory, StringComparison.Ordinal);

    /// <summary>Every <c>.razor</c>/<c>.cshtml</c>/<c>.json</c>/<c>.js</c> file under <paramref name="root"/>'s <c>src</c>, excluding build output.</summary>
    internal static List<string> ValueBearingFiles(string root) =>
        [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(path => SourceTree.IsProductionFile(path)
                && ValueBearingExtensions.Contains(Path.GetExtension(path), StringComparer.Ordinal))];

    /// <summary>The absolute path of a root-relative, forward-slash path.</summary>
    internal static string Absolute(string relativePath, string root) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The root-relative, forward-slash form of an absolute path, as the allowlist keys it.</summary>
    internal static string RelativePath(string path, string root) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The offender message for each hit in the file at <paramref name="path"/>: its repo-relative <c>path:line</c> and the id.</summary>
    internal static IEnumerable<string> Offenders(string path, IReadOnlyList<StringLiteral> hits) =>
        hits.Select(hit => $"{SourceTree.Locate(path, hit.Line)} '{hit.Text}'");
}
