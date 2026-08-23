using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler.Storage;

using Frontier.Platform.Workflow.Compiler.Schema;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// Chat designer service: persistent draft-scoped conversation, fetch→propose→diff→merge protocol.
/// Doc 14 §1–2: agent receives current draft + designer message, returns complete proposal + reasoning.
/// Phase 1: agent instructions (C-9) outline the contract; Phase 2 integrates MAF agents.
/// </summary>
public interface IChatDesignerService
{
    /// <summary>
    /// Submit a designer message, invoke the agent, persist the turn, and optionally merge the proposal.
    /// Returns the turn document with proposal (if agent succeeded) and merge outcome (if merge was attempted).
    /// </summary>
    Task<DesignTurnDocument> SubmitDesignTurnAsync(
        string workflowId,
        DesignTurnRequest request,
        CancellationToken ct);

    /// <summary>Get the chat history (metadata + turn list) for a workflow.</summary>
    Task<ChatHistoryData?> GetHistoryAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>Get all design turns for a workflow (for replay/audit).</summary>
    Task<IReadOnlyList<DesignTurnDocument>> GetAllTurnsAsync(
        string workflowId,
        CancellationToken ct);

    /// <summary>
    /// Author the system-authored welcome turn (turn 0) for a new draft (doc 14 §2): explains the
    /// describe → review → approve → auto-saved interaction model with engagement-type examples.
    /// Idempotent — does nothing if a chat history already exists for the workflow.
    /// </summary>
    Task EnsureWelcomeTurnAsync(
        string workflowId,
        string engagementType,
        CancellationToken ct);
}

/// <summary>Designer's turn submission request (message + optional explicit merge decision).</summary>
public sealed record DesignTurnRequest
{
    public required string DesignerId { get; init; }
    public required string Message { get; init; }
    public required bool AutoMergeProposal { get; init; } // If true and no conflicts, auto-merge

    /// <summary>
    /// Resource mentions the composer's `@`/`.` grammar inserted (doc 14 §8a, S9.33). Additive
    /// with an empty-list default — no <see cref="DesignTurnRequest"/> schema-version concern
    /// since this type isn't itself an <c>IVersionedContract</c>.
    /// </summary>
    public IReadOnlyList<ResourceMention> Mentions { get; init; } = [];
}

/// <summary>Chat history metadata + turn summaries.</summary>
public sealed record ChatHistoryData
{
    public required string WorkflowId { get; init; }
    public required string DraftId { get; init; }
    public required int TotalTurns { get; init; }
    public required DateTime LastMessageAtUtc { get; init; }
    public required IReadOnlyList<DesignTurnDocument> Turns { get; init; }
}
