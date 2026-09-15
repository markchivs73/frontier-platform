namespace Frontier.Platform.ModelRoleConfig;

/// <summary>
/// Pins the mapping version each role is served at execution start (doc 08 §5, ADR-PA29).
/// A separate seam from <see cref="IModelResolver"/> and <see cref="IRoleRegistry"/> so neither
/// published interface grows a member its consumers' test doubles would have to implement.
/// </summary>
public interface IMappingPinner
{
    /// <summary>
    /// Pins every role in <paramref name="roleIds"/> for <paramref name="engagementId"/>: canary
    /// assignment and fleet fallback are evaluated once, here. The result holds one entry per
    /// distinct role, ordered by role id (ordinal).
    /// </summary>
    /// <exception cref="Abstractions.ContractViolationException">A role has no mapping — a permanent failure, raised before any agent runs.</exception>
    Task<IReadOnlyList<ModelRolePin>> PinAsync(string engagementId, IEnumerable<string> roleIds, CancellationToken cancellationToken);
}
