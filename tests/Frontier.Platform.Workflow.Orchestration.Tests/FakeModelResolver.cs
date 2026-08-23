using Frontier.Platform.ModelRoleConfig;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>Configurable <see cref="IModelResolver"/> test double for S4.2 pipeline tests.</summary>
internal sealed class FakeModelResolver(ResolvedModel result) : IModelResolver
{
    /// <summary>The most recent <see cref="ResolutionRequest"/> passed to <see cref="ResolveAsync"/>.</summary>
    internal ResolutionRequest? ReceivedRequest { get; private set; }

    public Task<ResolvedModel> ResolveAsync(ResolutionRequest request, CancellationToken cancellationToken)
    {
        ReceivedRequest = request;
        return Task.FromResult(result);
    }
}
