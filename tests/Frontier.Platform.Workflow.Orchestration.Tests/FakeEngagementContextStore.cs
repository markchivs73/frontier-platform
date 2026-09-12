using Frontier.Platform.Abstractions;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IEngagementContextStore"/> test double for S4.2 composer tests.</summary>
internal sealed class FakeEngagementContextStore(string? dynamicContextJson) : IEngagementContextStore
{
    private int currentEpoch;

    public Task<string?> GetDynamicContextAsync(EngagementId engagementId, CancellationToken ct) => Task.FromResult(dynamicContextJson);

    public Task<int> UpsertDynamicContextAsync(EngagementId engagementId, string dynamicContent, CancellationToken ct) => Task.FromResult(++currentEpoch);

    /// <summary>
    /// This double serves a fixed document and cannot represent a write, so it refuses the merge
    /// rather than reporting a success that changed nothing (S13.62). The composer tests it serves
    /// never refresh; the merge semantics are pinned in <c>EngagementContextMergeTests</c>.
    /// </summary>
    public Task<int> MergeDynamicContextAsync(EngagementId engagementId, IReadOnlyDictionary<string, string> components, CancellationToken ct) =>
        throw new NotSupportedException("FakeEngagementContextStore serves fixed content; merge is exercised against the real stores.");

    /// <summary>The epoch the fake reports as current (S13.60); a request for any other epoch returns <see langword="null"/>, as the real stores do for a missing version.</summary>
    internal int CurrentEpoch { get; init; }

    /// <summary>The most recent epoch argument passed to <see cref="GetDynamicContextSnapshotAsync"/>.</summary>
    internal int? ReceivedEpoch { get; private set; }

    public Task<EngagementContextSnapshot?> GetDynamicContextSnapshotAsync(EngagementId engagementId, int? epoch, CancellationToken ct)
    {
        ReceivedEpoch = epoch;
        if (dynamicContextJson is null || (epoch is not null && epoch.Value != CurrentEpoch))
            return Task.FromResult<EngagementContextSnapshot?>(null);
        return Task.FromResult<EngagementContextSnapshot?>(new EngagementContextSnapshot(
            CurrentEpoch, $"{engagementId.Value}:ctx:e{CurrentEpoch:D6}", Frontier.Platform.Serialization.CanonicalProfile.Hash(dynamicContextJson), dynamicContextJson));
    }
}
