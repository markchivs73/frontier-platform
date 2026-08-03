
using Frontier.Platform.Serialization;

namespace Frontier.Platform.ContextAssembly;

/// <summary>
/// Diagnostic interface for dumping assembled context at each tier and cache directives.
/// Used for observability and troubleshooting context assembly during development/debugging.
/// Supports both text dumps and structured comparison results (C-24, doc 04 §11).
/// The Blazor `&lt;ContextDebugger&gt;` component (collapsible panes, tier-boundary diff view)
/// is deferred to Stage 9/doc 19 — this interface provides the data primitives only.
/// </summary>
public interface IContextDebugger
{
    /// <summary>
    /// Dump detailed context assembly information to a text writer.
    /// Includes all three tiers, byte counts, cache directives, and provider-specific layout.
    /// If a previous comparison result is provided, also includes delta markers vs. that prior state.
    /// </summary>
    /// <param name="executionId">The execution ID (for log correlation).</param>
    /// <param name="package">The assembled context package.</param>
    /// <param name="layout">The provider-specific message layout produced by the caching strategy.</param>
    /// <param name="output">Text writer to dump debug information to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DumpContextAsync(
        string executionId,
        ContextPackage package,
        ProviderMessageLayout layout,
        TextWriter output,
        CancellationToken ct);

    /// <summary>
    /// Compares a current context package against a previous one (for refresh debugging).
    /// Produces per-tier content hashes, cache verdicts derived from directives/metrics, and change markers.
    /// </summary>
    /// <param name="current">The current context package.</param>
    /// <param name="previous">The previous package (for delta comparison); null if no prior state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Structured comparison result with per-tier hashes, verdicts, and change flags.</returns>
    Task<ContextComparisonResult> CompareAsync(
        ContextPackage current,
        ContextPackage? previous,
        CancellationToken ct);
}
