using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Diagnostic dumper for assembled context (S3.5 debugging aid, S6.2c comparison).
/// Outputs detailed information about each tier, byte counts, and applied cache directives.
/// Supports structured comparison of context packages for observability.
/// </summary>
internal sealed class ContextDebugger : IContextDebugger
{
    /// <inheritdoc />
    public async Task DumpContextAsync(
        string executionId,
        ContextPackage package,
        ProviderMessageLayout layout,
        TextWriter output,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        await output.WriteLineAsync($"=== Context Debug Dump for Execution {executionId} ===");
        await output.WriteLineAsync();

        await output.WriteLineAsync("## Assembled Package");
        await output.WriteLineAsync();

        await output.WriteLineAsync("## Context Tiers");
        await output.WriteLineAsync($"  Baseline:  {package.Baseline.Content.Length:N0} bytes ({EstimateTokens(package.Baseline.Content)} tokens)");
        await output.WriteLineAsync($"  Dynamic:   {package.Dynamic.Content.Length:N0} bytes ({EstimateTokens(package.Dynamic.Content)} tokens)");
        if (package.RealTime is not null)
            await output.WriteLineAsync($"  Real-Time: {package.RealTime.Content.Length:N0} bytes ({EstimateTokens(package.RealTime.Content)} tokens)");
        await output.WriteLineAsync();

        await output.WriteLineAsync("## Cache Hints");
        await output.WriteLineAsync($"  Baseline breakpoint: {package.Hints.BreakpointAfterBaseline}");
        await output.WriteLineAsync($"  Dynamic breakpoint: {package.Hints.BreakpointAfterDynamic}");
        await output.WriteLineAsync();

        await output.WriteLineAsync("## Cache Directives");
        if (layout.CacheDirectives.Count > 0)
        {
            foreach (var directive in layout.CacheDirectives)
            {
                await output.WriteLineAsync($"  Tier: {directive.Tier}");
                await output.WriteLineAsync($"    Provider: {directive.Provider}");
                await output.WriteLineAsync($"    Strategy: {directive.Strategy}");
                if (directive.ExpiresAtUtc.HasValue)
                    await output.WriteLineAsync($"    Expires: {directive.ExpiresAtUtc:O}");
            }
        }
        else
        {
            await output.WriteLineAsync("  (none)");
        }
        await output.WriteLineAsync();

        await output.WriteLineAsync("## Provider Message Layout");
        await output.WriteLineAsync($"  System Messages: {layout.SystemMessages.Count}");
        await output.WriteLineAsync($"  User Messages: {layout.UserMessages.Count}");
        await output.WriteLineAsync($"  Estimated Tokens: {layout.EstimatedTokens:N0}");
        await output.WriteLineAsync($"  Refresh Reason: (none)");
        await output.WriteLineAsync();

        await output.WriteLineAsync("=== End Debug Dump ===");
    }

    /// <inheritdoc />
    public Task<ContextComparisonResult> CompareAsync(
        ContextPackage current,
        ContextPackage? previous,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        var currentBaselineHash = CanonicalProfile.Hash(current.Baseline.Content);
        var currentDynamicHash = CanonicalProfile.Hash(current.Dynamic.Content);
        var currentRealTimeHash = current.RealTime is not null
            ? CanonicalProfile.Hash(current.RealTime.Content)
            : null;

        var baselineChanged = previous is null || currentBaselineHash != CanonicalProfile.Hash(previous.Baseline.Content);
        var dynamicChanged = previous is null || currentDynamicHash != CanonicalProfile.Hash(previous.Dynamic.Content);
        var realTimeChanged = previous?.RealTime is null
            ? current.RealTime is not null
            : current.RealTime is null || currentRealTimeHash != CanonicalProfile.Hash(previous.RealTime.Content);

        var baselineVerdict = "cached";
        var dynamicVerdict = "cached";
        var realTimeVerdict = "not_cached";

        var comparison = new ContextComparisonResult(
            BaselineComparison: new(currentBaselineHash, baselineVerdict, baselineChanged),
            DynamicComparison: new(currentDynamicHash, dynamicVerdict, dynamicChanged, EpochIfAvailable: current.Dynamic.DynamicEpoch),
            RealTimeComparison: current.RealTime is not null
                ? new(currentRealTimeHash!, realTimeVerdict, realTimeChanged, current.RealTime.Fetches.Count)
                : null);

        return Task.FromResult(comparison);
    }

    /// <summary>
    /// Rough token estimate (4 chars per token).
    /// </summary>
    private static int EstimateTokens(string content) =>
        Math.Max(1, content.Length / 4);
}
