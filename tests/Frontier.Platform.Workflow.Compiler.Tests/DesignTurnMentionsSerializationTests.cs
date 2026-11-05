using System.Text.Json;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Compiler.Storage;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.224 (review-01 GOV-11): under the canonical profile a Cosmos client configured with
/// <c>UseSystemTextJsonSerializerWithOptions</c> uses, a design turn's mentions serialize as
/// <c>mentions</c> with each <c>resolved</c> flag, and read back unchanged.
/// </summary>
public sealed class DesignTurnMentionsSerializationTests
{
    [Fact]
    public void DesignTurn_RoundTripsMentionsAndTheirResolvedFlags_UnderTheCanonicalProfile()
    {
        var turn = DesignTurnFixture.WithMentions("wf-s13224");

        var json = JsonSerializer.Serialize(turn, CanonicalProfile.Options);
        var reloaded = JsonSerializer.Deserialize<DesignTurnDocument>(json, CanonicalProfile.Options)!;

        using var document = JsonDocument.Parse(json);
        var mentions = document.RootElement.GetProperty("mentions");
        Assert.Equal([true, false], mentions.EnumerateArray().Select(m => m.GetProperty("resolved").GetBoolean()));
        Assert.Equal(turn.Mentions, reloaded.Mentions);
    }
}
