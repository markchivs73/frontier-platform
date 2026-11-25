namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// What dropping a draft's design conversation removes and keeps (consumer S13.294, doc 14 §2): every turn but
/// the system-authored welcome turn (turn 0, which doc 14 §2 says is not deletable), and a chat-history index that
/// then lists only the welcome turn. The history's next turn number is kept, so turn ids are never reused. Kept
/// apart from the coverage-excluded Cosmos store so the selection is unit-tested.
/// </summary>
internal static class DesignConversationDrop
{
    /// <summary>The ids of the turns to delete: every turn after the welcome turn.</summary>
    internal static IReadOnlyList<string> TurnIdsToDelete(IReadOnlyList<DesignTurnDocument> turns) =>
        [.. turns.Where(t => t.TurnNumber > 0).Select(t => t.Id)];

    /// <summary><paramref name="history"/> listing only the welcome turn; null when there is no history to rewrite.</summary>
    internal static ChatHistoryDocument? KeptHistory(ChatHistoryDocument? history, IReadOnlyList<DesignTurnDocument> turns) =>
        history is null ? null : history with { TurnDocumentIds = [.. turns.Where(t => t.TurnNumber == 0).Select(t => t.Id)] };
}
