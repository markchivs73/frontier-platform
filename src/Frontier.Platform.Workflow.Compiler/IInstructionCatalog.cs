namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Resolves agent instruction references (e.g. <c>"instructions/gen-scope.md"</c>) for the
/// <c>agent.instructions-resolve</c> rule (doc 13 §4.2 R2, S9.30). A consumer-owned abstraction:
/// the implementation mirrors the runtime's instructions store (Orchestration's file-backed
/// resolver in Phase 1) and is wired only in the composition root, so a definition can never
/// publish an instructions_ref the runtime would fail to load.
/// </summary>
public interface IInstructionCatalog
{
    /// <summary>Whether <paramref name="instructionsRef"/> resolves to a stored instruction.</summary>
    Task<bool> ResolvesAsync(string instructionsRef, CancellationToken ct);

    /// <summary>
    /// The full set of resolvable instruction refs (e.g. <c>"instructions/fetch-ticket.md"</c>),
    /// so the chat designer can ground the agent on the real instruction set (S9.82) rather than
    /// letting it guess a ref — the exact pattern S9.72 used for the contract catalogue. Ordinal-sorted
    /// for stable prompt bytes.
    /// </summary>
    Task<IReadOnlyList<string>> ListRefsAsync(CancellationToken ct);
}
