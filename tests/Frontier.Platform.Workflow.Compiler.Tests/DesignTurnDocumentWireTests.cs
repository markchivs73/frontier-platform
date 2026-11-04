using System.Text.Json;

using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Storage;

using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.200: a turn cut off at the model's output ceiling carries <c>replyTruncated: true</c> on the
/// wire; a complete turn omits the field, so its stored bytes are what they were before the flag existed.
/// </summary>
public sealed class DesignTurnDocumentWireTests
{
    private static readonly DesignTurnDocument Turn = new()
    {
        Id = "wf-x:turn:1",
        WorkflowId = "wf-x",
        DraftId = "wf-x:draft",
        TurnNumber = 1,
        DesignerId = "d",
        CreatedAtUtc = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc),
        DesignerMessage = "add a step",
        DraftRevisionAtTurn = "r1",
    };

    [Fact]
    public void TruncatedTurn_WritesReplyTruncatedTrue()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Turn with { ReplyTruncated = true }, CanonicalProfile.Options));

        Assert.True(doc.RootElement.GetProperty("replyTruncated").GetBoolean());
    }

    [Fact]
    public void CompleteTurn_OmitsReplyTruncated()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Turn, CanonicalProfile.Options));

        Assert.False(doc.RootElement.TryGetProperty("replyTruncated", out _));
    }

    [Fact]
    public void ReplyTruncated_RoundTrips()
    {
        var json = JsonSerializer.Serialize(Turn with { ReplyTruncated = true }, CanonicalProfile.Options);

        Assert.True(JsonSerializer.Deserialize<DesignTurnDocument>(json, CanonicalProfile.Options)!.ReplyTruncated);
    }
}
