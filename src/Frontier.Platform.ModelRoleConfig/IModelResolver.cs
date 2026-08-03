namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Resolves a role to a concrete model (doc 08 §3): the only path
/// <c>InvokeAgentActivity</c> (S4.2) has from an <c>AgentTaskNode.Role</c> to a model it
/// can bind to. A thin seam over the role registry — ring/canary assignment and
/// fallback-chain walking live here (doc 08 §5).
/// </summary>
public interface IModelResolver
{
    /// <summary>Resolves <paramref name="request"/>'s role under its pinned (or active) mapping version.</summary>
    Task<ResolvedModel> ResolveAsync(ResolutionRequest request, CancellationToken cancellationToken);
}
