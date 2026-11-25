namespace Frontier.Platform.Workflow.Compiler.Storage;

/// <summary>
/// What dropping a draft's design conversation removes and keeps (consumer S13.294, doc 14 §2): every turn but
/// the system-authored welcome turn (turn 0, which doc 14 §2 says is not deletable), and a chat-history index
/// rewritten to leave out exactly the turns deleted. A turn written after the turns were read is neither deleted
/// nor dropped from the index. The history's next turn number is kept, so turn ids are never reused. Kept apart
/// from the coverage-excluded Cosmos store so the selection and the guarded rewrite are unit-tested.
/// </summary>
internal static class DesignConversationDrop
{
    /// <summary>How many times the index rewrite re-reads after losing a race before refusing.</summary>
    internal const int MaxHistoryRewrites = 3;

    /// <summary>The ids of the turns to delete: every turn after the welcome turn.</summary>
    internal static IReadOnlyList<string> TurnIdsToDelete(IReadOnlyList<DesignTurnDocument> turns) =>
        [.. turns.Where(t => t.TurnNumber > 0).Select(t => t.Id)];

    /// <summary><paramref name="history"/> without the ids in <paramref name="deletedIds"/>.</summary>
    internal static ChatHistoryDocument Without(ChatHistoryDocument history, IReadOnlyCollection<string> deletedIds) =>
        history with { TurnDocumentIds = [.. history.TurnDocumentIds.Where(id => !deletedIds.Contains(id))] };

    /// <summary>
    /// Rewrites the chat-history index without <paramref name="deletedIds"/>, guarded by the version it was read
    /// at: a turn appended concurrently makes the replace fail, and the index is re-read so that turn stays
    /// listed. Nothing to do when there is no index. Refuses after <see cref="MaxHistoryRewrites"/> lost races.
    /// </summary>
    /// <exception cref="InvalidOperationException">The index kept changing; the caller publishes nothing.</exception>
    internal static async Task RewriteHistoryAsync(
        Func<Task<(ChatHistoryDocument History, string Version)?>> read,
        Func<ChatHistoryDocument, string, Task<bool>> tryReplace,
        IReadOnlyCollection<string> deletedIds)
    {
        for (var attempt = 0; attempt < MaxHistoryRewrites; attempt++)
        {
            if (await read() is not { } current || await tryReplace(Without(current.History, deletedIds), current.Version))
                return;
        }

        throw new InvalidOperationException(
            $"The design conversation's history index changed {MaxHistoryRewrites} times while it was being dropped; nothing was published. Approve again.");
    }
}
