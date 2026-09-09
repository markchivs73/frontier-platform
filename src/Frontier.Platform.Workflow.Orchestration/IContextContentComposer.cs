using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// Fetches and filters the store-held context content a node's
/// <see cref="ContextRequest"/> names (doc 03 §2), producing the pre-composed tier
/// strings <see cref="AssembleContextActivity"/> requires. Context Assembly's stores
/// (<see cref="Frontier.Platform.ContextAssembly.IBaselineCatalogueStore"/>,
/// <see cref="Frontier.Platform.ContextAssembly.IEngagementContextStore"/>) hold
/// whole-catalogue/whole-engagement JSON; this composer is the agent-side caller that
/// narrows it to the requested fields — agents themselves never fetch (doc 00 §2.8).
/// </summary>
internal interface IContextContentComposer
{
    /// <summary>
    /// Composes the baseline/dynamic/real-time content strings for <paramref name="request"/>.
    /// <paramref name="revisionNote"/> feeds the <c>"hitl-revision-note"</c> real-time
    /// source (doc 06 §13, S4.6c) when <paramref name="request"/> asks for it.
    /// </summary>
    /// <param name="request">The node's context request.</param>
    /// <param name="revisionNote">A gate rejection's revision note, if the node is re-running after one.</param>
    /// <param name="pinnedEpoch">
    /// The dynamic-context epoch the run is pinned to (S13.60, doc 04 §8 "epoch N (frozen)"), or
    /// <see langword="null"/> to read whatever is current — the unpinned case, which after
    /// S13.60 means the run started with nothing stored.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ComposedContext> ComposeAsync(ContextRequest request, string? revisionNote, int? pinnedEpoch, CancellationToken ct);
}
