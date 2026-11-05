using Frontier.Platform.Workflow.Compiler.Storage;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>Design turns for the S13.224 persistence tests.</summary>
internal static class DesignTurnFixture
{
    /// <summary>A turn on <paramref name="workflowId"/> carrying one resolved and one unresolved mention.</summary>
    public static DesignTurnDocument WithMentions(string workflowId) => new()
    {
        Id = $"{workflowId}:turn:1",
        WorkflowId = workflowId,
        DraftId = $"{workflowId}:draft",
        TurnNumber = 1,
        DesignerId = "user:mark",
        CreatedAtUtc = new DateTime(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc),
        DesignerMessage = "Use @agent:drafter and @tool:missing",
        DraftRevisionAtTurn = "rev-0",
        Mentions =
        [
            new ValidatedMention { Kind = MentionKind.AgentRole, Ref = "drafter", Resolved = true },
            new ValidatedMention { Kind = MentionKind.McpTool, Ref = "missing", Resolved = false },
        ],
    };
}
