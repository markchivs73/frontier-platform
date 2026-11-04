using System.Text.RegularExpressions;
using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// K8 threat pattern "SDK types appearing on a public surface": no platform library may put a
/// type from a vendor or runtime SDK on its public API, except where the allowlist below names
/// the exposure and says why it is there.
/// <para>
/// The subject is each library's <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c>,
/// which the public-API analyzer keeps exactly in step with the compiled surface — so a new
/// exposure cannot reach the build without appearing in one of them.
/// </para>
/// </summary>
public sealed partial class PublicSurfaceSdkTypeTests
{
    /// <summary>
    /// Namespace prefixes that belong to an SDK rather than to the platform or the BCL. The
    /// <c>Microsoft.Extensions.DependencyInjection</c>/<c>Configuration</c>/<c>Options</c>/<c>Logging</c>
    /// hosting abstractions are not listed: they are the composition seam the <c>AddFrontierXxx()</c>
    /// extensions are written against, not an SDK a library could hide.
    /// </summary>
    internal static readonly IReadOnlyList<string> SdkNamespacePrefixes =
    [
        "Azure.",
        "Microsoft.Azure.",
        "Microsoft.DurableTask.",
        "Microsoft.Agents.",
        "Microsoft.Extensions.AI.",
        "ModelContextProtocol.",
        "Polly.",
        "OpenAI.",
        "Anthropic.",
        "OpenTelemetry.",
        "Newtonsoft.",
        "Corvus.",
    ];

    /// <summary>
    /// Each SDK type a library is allowed to expose, keyed <c>library|type</c>, with the reason it
    /// is on the surface. Named rather than counted: a new exposure is a failure until someone adds
    /// a row here with a reason a reviewer can challenge, and a row nothing uses any more fails too.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Frontier.Platform.Workflow.Orchestration|Microsoft.DurableTask.TaskActivityContext"] =
            "The activities derive from the DTF TaskActivity base class, whose RunAsync override fixes the signature.",
        ["Frontier.Platform.Workflow.Orchestration|Microsoft.DurableTask.TaskOrchestrationContext"] =
            "GraphOrchestrator and DispatcherOrchestrator derive from the DTF TaskOrchestrator base class, whose RunAsync override fixes the signature.",
        ["Frontier.Platform.Workflow.Orchestration|Microsoft.Extensions.AI.AITool"] =
            "IMcpToolCatalog hands MCP tools to the agent invocation in the shape the chat client takes.",
        ["Frontier.Platform.Workflow.Orchestration|Microsoft.Extensions.AI.ChatResponseFormat"] =
            "CanonicalOutputSchema produces the structured-output format the chat client takes.",
        ["Frontier.Platform.Workflow.Orchestration|Microsoft.Extensions.AI.UsageDetails"] =
            "Agent invocation outcomes carry the chat client's token usage unchanged.",
        ["Frontier.Platform.Resilience|Polly.CircuitBreaker.CircuitState"] =
            "Resilience owns the circuit breakers and reports their state as Polly defines it.",
        ["Frontier.Platform.Resilience|Polly.ResiliencePipeline"] =
            "IResiliencePolicyProvider hands out the pipeline a named retry profile builds.",
        ["Frontier.Platform.Resilience|Microsoft.DurableTask.TaskOptions"] =
            "IResiliencePolicyProvider turns a named retry profile into the DTF activity retry options.",
        ["Frontier.Platform.Workflow.Compiler|Microsoft.Azure.Cosmos.Container"] =
            "The Cosmos adapters (CosmosDefinitionStore, RetirementMonitor) take their container from the composition root.",
        ["Frontier.Platform.Guardrails|Microsoft.Azure.Cosmos.Container"] =
            "AddFrontierGuardrails takes the guard-rail ledger container from the composition root.",
    };

    /// <summary>A dotted, namespace-qualified name that does not continue an earlier one.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_.])[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+", RegexOptions.None, 2000)]
    private static partial Regex QualifiedName();

    /// <summary>Every SDK type on every library's public-API files is named by an allowlist row.</summary>
    [Fact]
    public void NoPublicSurfaceExposesAnUnlistedSdkType()
    {
        var unlisted = ScanAll().Where(use => !Allowlist.ContainsKey(use.Key)).Select(use => use.ToString()).ToList();

        Assert.True(unlisted.Count == 0,
            "A public platform surface exposes an SDK type (K8). Put a platform type or a consumer-owned port "
            + $"on the surface instead, or add an allowlist row with a reason.{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
    }

    /// <summary>Every allowlist row still matches an SDK type on some public surface.</summary>
    [Fact]
    public void EveryAllowlistRowIsStillUsed()
    {
        var used = ScanAll().Select(use => use.Key).ToHashSet(StringComparer.Ordinal);
        var stale = Allowlist.Keys.Where(key => !used.Contains(key)).ToList();

        Assert.True(stale.Count == 0,
            $"These allowlist rows match nothing on any public surface; remove them.{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    /// <summary>The scan must be able to fire — a check certifying nothing looks exactly like a check finding nothing.</summary>
    [Fact]
    public void ScanFindsSdkTypesInKnownBadLines()
    {
        var uses = Scan("Lib", "PublicAPI.Unshipped.txt",
        [
            "Lib.Store.Store(Microsoft.Azure.Cosmos.Container! container) -> void",
            "Lib.Catalog.Tools.get -> System.Collections.Generic.IReadOnlyList<ModelContextProtocol.Client.McpClientTool!>!",
        ]);

        Assert.Equal(["Lib|Microsoft.Azure.Cosmos.Container", "Lib|ModelContextProtocol.Client.McpClientTool"], uses.Select(u => u.Key));
        Assert.Equal(2, uses[1].Line);
        Assert.EndsWith("PublicAPI.Unshipped.txt:2: ModelContextProtocol.Client.McpClientTool", uses[1].ToString(), StringComparison.Ordinal);
    }

    /// <summary>Every allowlist row gives the reason the exposure is on the surface.</summary>
    [Fact]
    public void EveryAllowlistRowHasAReason() =>
        Assert.All(Allowlist, row => Assert.False(string.IsNullOrWhiteSpace(row.Value), row.Key));

    /// <summary>Each listed prefix is treated as an SDK namespace when it names a type on a surface line.</summary>
    /// <param name="prefix">One entry of <see cref="SdkNamespacePrefixes"/>.</param>
    [Theory]
    [MemberData(nameof(Prefixes))]
    public void ScanFindsATypeUnderEachSdkPrefix(string prefix)
    {
        var uses = Scan("Lib", "PublicAPI.Shipped.txt", [$"Lib.Thing.Get() -> {prefix}Vendor.Widget!"]);

        Assert.Equal([$"Lib|{prefix}Vendor.Widget"], uses.Select(u => u.Key));
    }

    /// <summary>The listed SDK prefixes, as theory data.</summary>
    public static TheoryData<string> Prefixes()
    {
        var data = new TheoryData<string>();
        foreach (var prefix in SdkNamespacePrefixes)
        {
            data.Add(prefix);
        }

        return data;
    }

    /// <summary>Names outside the SDK prefixes — BCL, hosting abstractions, and a platform namespace that merely contains an SDK word — are not reported.</summary>
    /// <param name="line">A public-API line with no SDK type on it.</param>
    [Theory]
    [InlineData("Lib.Options.Key.get -> string!")]
    [InlineData("static Lib.Ext.AddLib(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> void")]
    [InlineData("Lib.Azure.Thing.Name.get -> string!")]
    [InlineData("Lib.Thing.Run(System.Threading.CancellationToken ct) -> System.Threading.Tasks.Task!")]
    public void ScanIgnoresNonSdkNames(string line) =>
        Assert.Empty(Scan("Lib", "PublicAPI.Shipped.txt", [line]));

    /// <summary>The scan reads both public-API files of exactly the governed platform libraries.</summary>
    [Fact]
    public void ScanCoversEveryPlatformLibrary()
    {
        var libraries = PublicApiFiles().Select(file => file.Library).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(GovernedAssemblies.PlatformLibraryNames.Order(StringComparer.Ordinal), libraries.Order(StringComparer.Ordinal));
        Assert.Equal(GovernedAssemblies.PlatformLibraryNames.Count * 2, PublicApiFiles().Count);
    }

    /// <summary>Every SDK type use on every library's public-API files.</summary>
    private static List<SdkTypeUse> ScanAll() =>
        [.. PublicApiFiles().SelectMany(file => Scan(file.Library, file.Path, File.ReadAllLines(file.Path)))];

    /// <summary>The SDK type uses in <paramref name="lines"/>, attributed to <paramref name="library"/>.</summary>
    internal static List<SdkTypeUse> Scan(string library, string path, IReadOnlyList<string> lines) =>
        [.. lines.SelectMany((line, index) => QualifiedName().Matches(line)
            .Select(match => match.Value)
            .Where(IsSdkName)
            .Select(name => new SdkTypeUse(library, name, path, index + 1)))];

    /// <summary>Whether <paramref name="qualifiedName"/> starts with one of <see cref="SdkNamespacePrefixes"/>.</summary>
    internal static bool IsSdkName(string qualifiedName) =>
        SdkNamespacePrefixes.Any(prefix => qualifiedName.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Both public-API files of each library under <c>src</c>, with the library they describe.</summary>
    private static List<(string Library, string Path)> PublicApiFiles() =>
        [.. Directory.EnumerateDirectories(Path.Combine(SourceTree.RepositoryRoot(), "src"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "PublicAPI.*.txt"))
            .Select(path => (Library: Path.GetFileName(Path.GetDirectoryName(path)!), Path: path))
            .OrderBy(file => file.Path, StringComparer.Ordinal)];

    /// <summary>One SDK type named on one line of a library's public-API file.</summary>
    internal sealed record SdkTypeUse(string Library, string TypeName, string Path, int Line)
    {
        /// <summary>The allowlist key: the library and the SDK type it exposes.</summary>
        public string Key => $"{Library}|{TypeName}";

        /// <inheritdoc />
        public override string ToString() => $"{SourceTree.Locate(Path, Line)}: {TypeName}";
    }
}
