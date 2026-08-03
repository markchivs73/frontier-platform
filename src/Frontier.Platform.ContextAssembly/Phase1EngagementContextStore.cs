using System.Collections.Concurrent;
using System.Text.Json;
using Frontier.Platform.Abstractions;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// In-memory <see cref="IEngagementContextStore"/> seeded from
/// <see cref="Phase1ContextCatalogue.EngagementContextJson"/> (registered by
/// <see cref="ContextAssemblyServiceCollectionExtensions.AddFrontierContextAssembly"/> as a
/// singleton, so <see cref="UpsertDynamicContextAsync"/> writes are visible to later
/// reads within the same process). A Cosmos-backed implementation
/// (<c>/engagementId</c> partition key, cosmos-conventions) replaces this once the
/// dynamic tier needs to persist across process restarts.
/// </summary>
internal sealed class Phase1EngagementContextStore : IEngagementContextStore
{
    /// <summary>
    /// Prefix of the ephemeral engagement ids the sandbox test-run channel mints
    /// (<c>TestRunService</c> → <c>SANDBOX-{guid}</c>, doc 13 §5). These are never seeded and — in
    /// the split api/worker topology — can't be seeded cross-process into this per-process store, so
    /// this PoC store hands them a default brief on read (S9.83) rather than leaving the entry node's
    /// runtime <c>EngagementBriefSection</c> bridging with nothing to read. Real engagements resolve
    /// from the seeded catalogue above, or return <see langword="null"/> as before.
    /// </summary>
    internal const string SandboxEngagementPrefix = "SANDBOX-";

    /// <summary>The default brief seeded for a sandbox engagement — enough for the entry node's bridging to resolve; the durable fix (Cosmos store / inline sample input) is a follow-up.</summary>
    internal const string SandboxDefaultBrief = "Sandbox test run: exercise this workflow end to end with representative data.";

    private readonly ConcurrentDictionary<string, string> _dynamicContext = new(Phase1ContextCatalogue.EngagementContextJson, StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _epochs = new();

    /// <inheritdoc />
    public Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engagementId);
        if (string.IsNullOrWhiteSpace(engagementId.Value))
            throw new ArgumentException("Engagement ID cannot be whitespace.", nameof(engagementId));

        var key = engagementId.Value;
        if (_dynamicContext.TryGetValue(key, out var json))
            return Task.FromResult<string?>(json);

        if (key.StartsWith(SandboxEngagementPrefix, StringComparison.Ordinal))
            return Task.FromResult<string?>(
                $$"""{"engagement_brief":{{JsonSerializer.Serialize(SandboxDefaultBrief)}}}""");

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engagementId);
        ArgumentNullException.ThrowIfNull(dynamicContent);
        if (string.IsNullOrWhiteSpace(engagementId.Value))
            throw new ArgumentException("Engagement ID cannot be whitespace.", nameof(engagementId));

        var key = engagementId.Value;
        _dynamicContext[key] = dynamicContent;
        var epoch = _epochs.AddOrUpdate(key, 0, (_, e) => e + 1);
        return Task.FromResult(epoch);
    }
}
