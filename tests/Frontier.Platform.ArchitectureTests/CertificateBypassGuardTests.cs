using Frontier.Platform.ArchitectureTests.Determinism;

namespace Frontier.Platform.ArchitectureTests;

/// <summary>
/// S13.349 (AUTHN-1): the one place a client may stop validating server certificates is the local emulator, and
/// "local" is decided by <c>LocalEndpoint.IsLoopback</c> alone. A second "is it local?" test, or an unguarded bypass,
/// is how the prefix rule came back four times; this scan stops it coming back again.
/// </summary>
public sealed class CertificateBypassGuardTests
{
    private const string Bypass = "DangerousAcceptAnyServerCertificateValidator";
    private const string Guard = "LocalEndpoint.IsLoopback(";

    [Fact]
    public void EveryCertificateBypass_IsGuardedByTheLoopbackRule()
    {
        var bypassing = SourceTree.ProductionFiles().Where(path => File.ReadAllText(path).Contains(Bypass, StringComparison.Ordinal)).ToList();
        var unguarded = bypassing.SelectMany(path => UnguardedIn(path, File.ReadAllText(path))).ToList();

        // The four Cosmos factories (Audit, Hitl, ModelRoleConfig, ContextAssembly): a scan finding none certifies nothing.
        Assert.Equal(4, bypassing.Count);

        Assert.True(unguarded.Count == 0,
            $"A certificate-validation bypass must sit in a conditional on {Guard}...) (S13.349).{Environment.NewLine}"
            + string.Join(Environment.NewLine, unguarded));
    }

    /// <summary>The rule must be able to fire: a bypass without the guard, or guarded by a prefix test, is reported.</summary>
    [Fact]
    public void TheRule_RejectsKnownBadSource()
    {
        Assert.Single(UnguardedIn("bad.cs", "HttpClientFactory = () => new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator });"));
        Assert.Single(UnguardedIn("bad.cs", "HttpClientFactory = endpoint.StartsWith(\"https://localhost\") ? () => new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }) : null;"));
        Assert.Empty(UnguardedIn("good.cs", "HttpClientFactory = LocalEndpoint.IsLoopback(options.Endpoint)\n    ? () => new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })\n    : null"));
    }

    /// <summary>
    /// Each bypass in <paramref name="text"/> whose statement does not name the guard. The statement is read back to
    /// the nearer of the previous <c>;</c> and the previous <c>HttpClientFactory</c>.
    /// </summary>
    internal static IEnumerable<string> UnguardedIn(string path, string text)
    {
        for (var at = text.IndexOf(Bypass, StringComparison.Ordinal); at >= 0; at = text.IndexOf(Bypass, at + Bypass.Length, StringComparison.Ordinal))
        {
            var start = Math.Max(text.LastIndexOf(';', at), text.LastIndexOf("HttpClientFactory", at, StringComparison.Ordinal));
            if (!text[Math.Max(start, 0)..at].Contains(Guard, StringComparison.Ordinal))
                yield return $"{path}: certificate bypass at offset {at} is not guarded by {Guard}...)";
        }
    }
}
