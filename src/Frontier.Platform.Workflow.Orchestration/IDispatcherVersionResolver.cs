using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Resolves the definition version a dispatcher's next generation should run (S13.18, ADR-E15 D2,
/// doc 16 §8).
/// <para>
/// <b>A port, not an implementation — and deliberately not DI-registered by the engine.</b> What a
/// published version <em>is</em>, and what an engagement's pin means, is the consumer's knowledge
/// (doc 16 §8, ADR-E7), exactly as <see cref="IDynamicContextContentProducer"/> is. Registering a
/// default here would let a misconfigured deployment roll a dispatcher onto the wrong definition
/// instead of failing to start.
/// </para>
/// <para>
/// Implementations are reached only from <see cref="ResolveDispatcherVersionActivity"/> — an
/// activity, so I/O is expected and allowed. Nothing on this interface may appear in an
/// orchestrator body (hard invariant 2).
/// </para>
/// </summary>
public interface IDispatcherVersionResolver
{
    /// <summary>
    /// Resolves the definition the next generation should run.
    /// </summary>
    /// <param name="engagementId">The engagement whose per-engagement pin governs the resolution.</param>
    /// <param name="workflowId">The workflow whose published versions are being resolved.</param>
    /// <param name="currentVersion">The definition version the ending generation was pinned to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The successor definition, or <see langword="null"/> meaning <b>no successor exists</b> — the
    /// dispatcher continues on the version it already has.
    /// </returns>
    /// <remarks>
    /// <b>Failure must throw; it must never be reported as <see langword="null"/>.</b> A store that
    /// is unreachable, a pin that cannot be read, a definition that will not deserialize — each of
    /// those is an error, and returning <see langword="null"/> for one would be indistinguishable
    /// from "nothing newer". The dispatcher would then carry on at its current version, silently
    /// and forever, while every generation repeated the same failed lookup: the silent-fallback
    /// shape that produced the epoch-0 no-op defect (ADR-PA24) and the S13.66 key-provider defect.
    /// An eternal instance has no later moment at which the mistake surfaces, so it must surface
    /// here.
    /// </remarks>
    Task<WorkflowDefinition?> ResolveAsync(string engagementId, string workflowId, int currentVersion, CancellationToken ct);
}
