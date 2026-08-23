using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Supplies the context component names the <c>context.known-components</c> rule validates
/// against (doc 13 §4.2 R2, doc 04 §10, S9.30). A consumer-owned abstraction: the implementation
/// adapts the Context Assembly library's baseline catalogue and Phase-1 engagement context
/// catalogue and is wired only in the composition root, so the Definition Compiler stays within
/// its library boundary (same pattern as <see cref="IAgentRoleCatalog"/>, S9.27c).
/// </summary>
public interface IContextComponentCatalog
{
    /// <summary>Baseline component names a <c>ContextRequest.BaselineComponents</c> entry may reference.</summary>
    Task<IReadOnlyCollection<string>> GetBaselineComponentNamesAsync(CancellationToken ct);

    /// <summary>Dynamic field names a <c>ContextRequest.DynamicFields</c> entry may reference.</summary>
    Task<IReadOnlyCollection<string>> GetDynamicFieldNamesAsync(CancellationToken ct);
}
