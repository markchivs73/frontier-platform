using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>S13.294 (doc 14 §2, ADR-CD1): what dropping a design conversation removes and keeps, and how the switch is set.</summary>
public sealed class DesignConversationDropTests
{
    private static DesignTurnDocument Turn(int number) => new()
    {
        Id = $"wf-1:turn:{number}", WorkflowId = "wf-1", DraftId = "wf-1:draft", TurnNumber = number,
        DesignerId = number == 0 ? "system" : "user:designer", CreatedAtUtc = DateTime.UtcNow,
        DesignerMessage = "m", DraftRevisionAtTurn = "rev",
    };

    private static readonly IReadOnlyList<DesignTurnDocument> Turns = [Turn(0), Turn(1), Turn(2)];

    private static ChatHistoryDocument History() => new()
    {
        Id = "wf-1:chat-history", WorkflowId = "wf-1", DraftId = "wf-1:draft", NextTurnNumber = 3,
        LastMessageAtUtc = DateTime.UtcNow, TurnDocumentIds = ["wf-1:turn:0", "wf-1:turn:1", "wf-1:turn:2"],
    };

    [Fact]
    public void TurnIdsToDelete_EveryTurnButTheWelcomeTurn() =>
        Assert.Equal(["wf-1:turn:1", "wf-1:turn:2"], DesignConversationDrop.TurnIdsToDelete(Turns));

    [Fact]
    public void KeptHistory_ListsOnlyTheWelcomeTurn_AndKeepsTheNextTurnNumber()
    {
        var kept = DesignConversationDrop.KeptHistory(History(), Turns)!;

        Assert.Equal(["wf-1:turn:0"], kept.TurnDocumentIds);
        Assert.Equal(3, kept.NextTurnNumber);
    }

    [Fact]
    public void KeptHistory_WithoutAWelcomeTurn_ListsNothing() =>
        Assert.Empty(DesignConversationDrop.KeptHistory(History(), [Turn(1)])!.TurnDocumentIds);

    [Fact]
    public void KeptHistory_NoHistory_IsNull() =>
        Assert.Null(DesignConversationDrop.KeptHistory(null, Turns));

    [Fact]
    public async Task DeleteDesignConversationAsync_DefaultImplementation_ThrowsNotSupported()
    {
        IDefinitionStore store = new InMemoryDefinitionStore();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => store.DeleteDesignConversationAsync("wf-1", CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.DeleteDesignConversationAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_WithoutTheSetting_KeepHistory() =>
        Assert.True(Bound(new Dictionary<string, string?>()).HistoryEnabled);

    [Fact]
    public void Options_ChatDesignerHistoryEnabledFalse_IsBound() =>
        Assert.False(Bound(new Dictionary<string, string?> { ["ChatDesigner:HistoryEnabled"] = "false" }).HistoryEnabled);

    /// <summary>The options <see cref="CompilerServiceCollectionExtensions.AddFrontierWorkflowCompiler(IServiceCollection, IConfiguration)"/> binds from <paramref name="settings"/>.</summary>
    private static ChatDesignerHistoryOptions Bound(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddFrontierWorkflowCompiler(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ChatDesignerHistoryOptions>>().Value;
    }
}
