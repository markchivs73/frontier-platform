namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Supplies the model the chat designer agent runs on (doc 14 §3: "agent invocations use
/// the <c>deep-reasoning</c> role", S9.9a — replaces the interim hardcoded model id). A
/// consumer-owned abstraction: the implementation adapts the Model-Role Config subsystem's
/// resolver and is wired only in the composition root, so the Definition Compiler stays
/// within its library boundary — mirrors <see cref="IAgentRoleCatalog"/> (S9.27) and
/// <see cref="ITestRunExecutor"/> (S9.38a).
/// </summary>
public interface IDesignerModelProvider
{
    /// <summary>
    /// Resolves the model for a design session on <paramref name="workflowId"/>. The
    /// workflow id is the resolver's stability key (a design session has no engagement),
    /// so a canary-ring mapping assigns a whole workflow's design sessions together.
    /// </summary>
    Task<DesignerModelSelection> GetAsync(string workflowId, CancellationToken ct);
}

/// <summary>The resolved designer model (S9.9a) — the consumer-owned projection of a Model-Role Config resolution.</summary>
public sealed record DesignerModelSelection
{
    /// <summary>The provider model id to request.</summary>
    public required string ModelId { get; init; }

    /// <summary>The resolved mapping entry's output-token ceiling.</summary>
    public required int MaxOutputTokens { get; init; }

    /// <summary>Whether to request the provider's adaptive-thinking mode (doc 14 §3).</summary>
    public required bool AdaptiveThinking { get; init; }
}
