using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The three pre-composed tier content strings produced by
/// <see cref="IContextContentComposer"/> for one <see cref="Abstractions.ContextRequest"/>,
/// ready to pass as <see cref="AssembleContextRequest"/>'s
/// <c>baseline_content</c>/<c>dynamic_content</c>/<c>real_time_content</c> fields.
/// </summary>
internal sealed record ComposedContext
{
    /// <summary>Canonical-JSON object of the requested baseline catalogue components.</summary>
    internal required string BaselineContent { get; init; }

    /// <summary>Canonical-JSON object of the requested dynamic engagement-context fields.</summary>
    internal required string DynamicContent { get; init; }

    /// <summary>
    /// Canonical-JSON object of the requested real-time fetches. <c>"{}"</c> unless
    /// <see cref="Abstractions.ContextRequest.RealTimeSources"/> requests
    /// <c>"hitl-revision-note"</c> and a revision note is available (doc 06 §13, S4.6c) —
    /// the only real-time source wired up so far; doc 04 §3's general MCP fetchers remain
    /// future work.
    /// </summary>
    internal required string RealTimeContent { get; init; }

    /// <summary>The dynamic-context epoch that was read (S13.60), or <see langword="null"/> when the engagement had none stored.</summary>
    internal int? DynamicEpoch { get; init; }

    /// <summary>The epoch document id the dynamic tier was read from, or <see langword="null"/> when none was stored.</summary>
    internal string? DynamicRef { get; init; }

    /// <summary>The store's content hash of the dynamic context read, or <see langword="null"/> when none was stored.</summary>
    internal string? DynamicContentHash { get; init; }
}
