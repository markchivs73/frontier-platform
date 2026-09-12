namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Renders an engagement's dynamic-context components on an ADR-CR1 refresh (S13.62, doc 18 §1).
/// <para>
/// <b>A port, not an implementation.</b> The components themselves — <c>engagement_profile</c>,
/// <c>client_profile</c>, <c>engagement_activity</c> — are consumer-side types over the consumer's
/// engagement entity and its CRM enrichment (doc 18 §1's "thin readers", ADR-EC1). The engine knows
/// only that a refresh needs rendered content and asks for it; what an engagement <em>is</em> is
/// not the interpreter's business, and recreating those components here would be an ADR-PA2 breach.
/// </para>
/// <para>
/// Implementations are reached only from <see cref="RefreshDynamicContextActivity"/> — an activity,
/// so I/O is expected and allowed. Nothing on this interface may appear in an orchestrator body
/// (hard invariant 2).
/// </para>
/// </summary>
public interface IDynamicContextContentProducer
{
    /// <summary>
    /// Renders the named <paramref name="components"/> for <paramref name="engagementId"/>,
    /// returning each one's canonical JSON keyed by its <b>snake_case document key</b> — the key it
    /// will be merged under, matching <see cref="Abstractions.DynamicContextRefreshRequired.Components"/>.
    /// </summary>
    /// <param name="engagementId">The engagement to render for.</param>
    /// <param name="components">
    /// The component keys the signal named, or <see langword="null"/> when it named none — the
    /// producer then decides the scope of a refresh for this <paramref name="refreshReason"/>.
    /// </param>
    /// <param name="refreshReason">ADR-CR1's explicit reason, in case rendering depends on what changed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Component key → rendered canonical JSON. Only the keys returned are written; every other key
    /// of the engagement's dynamic context survives byte-identically (ADR-PA24), so returning an
    /// empty map is a well-formed "nothing to change" and never wipes the document.
    /// </returns>
    Task<IReadOnlyDictionary<string, string>> ProduceAsync(
        string engagementId,
        IReadOnlyList<string>? components,
        string refreshReason,
        CancellationToken ct);
}
