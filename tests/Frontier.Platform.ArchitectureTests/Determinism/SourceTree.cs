using System.Reflection;

namespace Frontier.Platform.ArchitectureTests.Determinism;

/// <summary>
/// Locates the repository's <c>src</c> tree from the test binary, for rules whose subject is the
/// source text rather than the emitted IL — a call's <em>arguments</em> are the question for the
/// canonical-profile rule, and IL cannot say which options instance reached a call site.
/// </summary>
internal static class SourceTree
{
    /// <summary>Every <c>.cs</c> file under <c>src</c>, excluding build output.</summary>
    internal static IReadOnlyList<string> ProductionFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionFile)
            .OrderBy(p => p, StringComparer.Ordinal)];

    internal static bool IsProductionFile(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>Walks up from the test binary to the directory holding the central package versions.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test binary.");
    }

    /// <summary>A repo-relative <c>path:line</c> reference, so a failure is clickable.</summary>
    internal static string Locate(string path, int lineNumber) =>
        $"{Path.GetRelativePath(RepositoryRoot(), path)}:{lineNumber}";
}
