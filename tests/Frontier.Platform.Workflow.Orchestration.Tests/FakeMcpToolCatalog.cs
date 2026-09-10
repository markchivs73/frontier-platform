using Microsoft.Extensions.AI;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IMcpToolCatalog"/> test double for S9.25 pipeline tests.</summary>
internal sealed class FakeMcpToolCatalog(IReadOnlyList<AITool>? tools = null) : IMcpToolCatalog
{
    /// <summary>The most recent <c>toolRefs</c> passed to <see cref="ResolveAsync"/>.</summary>
    internal IReadOnlyList<string>? ReceivedToolRefs { get; private set; }

    /// <summary>The most recent <c>engagementId</c> passed to <see cref="ResolveAsync"/>.</summary>
    internal string? ReceivedEngagementId { get; private set; }

    public Task<IReadOnlyList<AITool>> ResolveAsync(IReadOnlyList<string> toolRefs, string engagementId, CancellationToken ct)
    {
        ReceivedToolRefs = toolRefs;
        ReceivedEngagementId = engagementId;
        return Task.FromResult(tools ?? []);
    }
}
