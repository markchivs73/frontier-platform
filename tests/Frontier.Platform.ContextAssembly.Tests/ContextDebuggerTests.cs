namespace Frontier.Platform.ContextAssembly.Tests;

/// <summary>Tests for <see cref="ContextDebugger"/> (S3.5 debugging aid).</summary>
public sealed class ContextDebuggerTests
{
    private readonly ContextDebugger debugger = new();

    [Fact]
    public async Task DumpContextAsync_NullOutput_Throws()
    {
        var package = ContextAssemblyTestData.Package();
        var layout = new ProviderMessageLayout(
            SystemMessages: Array.Empty<object>(),
            UserMessages: Array.Empty<object>(),
            CacheDirectives: Array.Empty<ProviderCacheDirective>(),
            EstimatedTokens: 0);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            debugger.DumpContextAsync("exec-1", package, layout, null!, CancellationToken.None));
    }

    [Fact]
    public async Task DumpContextAsync_NoCacheDirectives_WritesNoneMarker()
    {
        var package = ContextAssemblyTestData.Package();
        var layout = new ProviderMessageLayout(
            SystemMessages: Array.Empty<object>(),
            UserMessages: Array.Empty<object>(),
            CacheDirectives: Array.Empty<ProviderCacheDirective>(),
            EstimatedTokens: 42);

        using var writer = new StringWriter();
        await debugger.DumpContextAsync("exec-1", package, layout, writer, CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("exec-1", output, StringComparison.Ordinal);
        Assert.Contains("(none)", output, StringComparison.Ordinal);
        Assert.Contains("Refresh Reason: (none)", output, StringComparison.Ordinal);
        Assert.Contains("Estimated Tokens: 42", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumpContextAsync_WithCacheDirectives_WritesDirectiveDetails()
    {
        var package = ContextAssemblyTestData.Package();
        var layout = new ProviderMessageLayout(
            SystemMessages: Array.Empty<object>(),
            UserMessages: Array.Empty<object>(),
            CacheDirectives:
            [
                new ProviderCacheDirective("baseline", "anthropic", "explicit", ExpiresAtUtc: new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc)),
                new ProviderCacheDirective("dynamic", "anthropic", "none", ExpiresAtUtc: null),
            ],
            EstimatedTokens: 10);

        using var writer = new StringWriter();
        await debugger.DumpContextAsync("exec-1", package, layout, writer, CancellationToken.None);

        var output = writer.ToString();
        Assert.Contains("Tier: baseline", output, StringComparison.Ordinal);
        Assert.Contains("Provider: anthropic", output, StringComparison.Ordinal);
        Assert.Contains("Strategy: explicit", output, StringComparison.Ordinal);
        Assert.Contains("Expires:", output, StringComparison.Ordinal);
        Assert.Contains("Tier: dynamic", output, StringComparison.Ordinal);
    }
}
