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
}
