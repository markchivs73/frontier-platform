using Frontier.Platform.Abstractions;
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
    public void Without_LeavesOutTheDeletedTurns_KeepingTheWelcomeTurnAndTheNextTurnNumber()
    {
        var kept = DesignConversationDrop.Without(History(), ["wf-1:turn:1", "wf-1:turn:2"]);

        Assert.Equal(["wf-1:turn:0"], kept.TurnDocumentIds);
        Assert.Equal(3, kept.NextTurnNumber);
    }

    [Fact]
    public async Task RewriteHistoryAsync_NoHistory_ReplacesNothing()
    {
        var replaced = 0;

        await DesignConversationDrop.RewriteHistoryAsync(
            () => Task.FromResult<(ChatHistoryDocument, string)?>(null),
            (_, _) => { replaced++; return Task.FromResult(true); },
            ["wf-1:turn:1"]);

        Assert.Equal(0, replaced);
    }

    /// <summary>A turn appended while the drop ran makes the guarded replace fail; the re-read keeps that turn listed.</summary>
    [Fact]
    public async Task RewriteHistoryAsync_LosesARaceToANewTurn_ReReadsAndKeepsTheNewTurn()
    {
        var reads = new Queue<(ChatHistoryDocument, string)?>([(History(), "v1"), (History() with { NextTurnNumber = 4, TurnDocumentIds = [.. History().TurnDocumentIds, "wf-1:turn:3"] }, "v2")]);
        var written = new List<(ChatHistoryDocument History, string Version)>();

        await DesignConversationDrop.RewriteHistoryAsync(
            () => Task.FromResult(reads.Dequeue()),
            (history, version) => { written.Add((history, version)); return Task.FromResult(version == "v2"); },
            ["wf-1:turn:1", "wf-1:turn:2"]);

        Assert.Equal(["v1", "v2"], written.Select(w => w.Version));
        Assert.Equal(["wf-1:turn:0", "wf-1:turn:3"], written[^1].History.TurnDocumentIds);
        Assert.Equal(4, written[^1].History.NextTurnNumber);
    }

    [Fact]
    public async Task RewriteHistoryAsync_LosesEveryRace_RefusesAfterTheBound()
    {
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DesignConversationDrop.RewriteHistoryAsync(
            () => Task.FromResult<(ChatHistoryDocument, string)?>((History(), "v")),
            (_, _) => { attempts++; return Task.FromResult(false); },
            ["wf-1:turn:1"]));

        Assert.Equal(DesignConversationDrop.MaxHistoryRewrites, attempts);
        Assert.Contains("nothing was published", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The test fake drops as the Cosmos store does: the welcome turn and its index entry stay, and a second drop is not an error.</summary>
    [Fact]
    public async Task InMemoryStore_DropsEveryTurnButTheWelcome_AndDroppingAgainIsNotAnError()
    {
        var store = new InMemoryDefinitionStore();
        foreach (var turn in Turns)
            await store.PersistDesignTurnAsync(turn, CancellationToken.None);
        await store.CreateOrUpdateChatHistoryAsync(History(), CancellationToken.None);

        await store.DeleteDesignConversationAsync("wf-1", CancellationToken.None);
        await store.DeleteDesignConversationAsync("wf-1", CancellationToken.None);

        Assert.Equal("wf-1:turn:0", Assert.Single(await store.GetAllDesignTurnsAsync("wf-1", CancellationToken.None)).Id);
        Assert.Equal(["wf-1:turn:0"], (await store.GetChatHistoryAsync("wf-1", CancellationToken.None))!.TurnDocumentIds);
    }

    [Fact]
    public async Task DeleteDesignConversationAsync_DefaultImplementation_ThrowsNotSupported()
    {
        IDefinitionStore store = new Moq.Mock<IDefinitionStore> { CallBase = true }.Object;

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => store.DeleteDesignConversationAsync("wf-1", CancellationToken.None));

        Assert.Contains(nameof(IDefinitionStore.DeleteDesignConversationAsync), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_WithoutTheSetting_KeepHistory() =>
        Assert.True(Bound(new Dictionary<string, string?>()).HistoryEnabled);

    [Fact]
    public void Options_ChatDesignerHistoryEnabledFalse_IsBound() =>
        Assert.False(Bound(new Dictionary<string, string?> { ["ChatDesigner:HistoryEnabled"] = "false" }).HistoryEnabled);

    /// <summary>Marked obsolete itself so it may call the obsolete registration, which must register the default too.</summary>
    [Fact]
    [Obsolete("Exercises the obsolete parameterless AddFrontierWorkflowCompiler.")]
    public void Options_TheObsoleteRegistration_KeepsHistory()
    {
        using var provider = new ServiceCollection().AddFrontierWorkflowCompiler().BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<ChatDesignerHistoryOptions>>().Value.HistoryEnabled);
    }

    /// <summary>The composition root builds the lifecycle service with the bound setting.</summary>
    [Fact]
    public void LifecycleService_FromTheContainer_CarriesTheBoundSetting()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ChatDesigner:HistoryEnabled"] = "false" }).Build();
        var services = new ServiceCollection().AddFrontierWorkflowCompiler(configuration);
        services.AddSingleton(new Moq.Mock<IDefinitionStore>().Object);
        services.AddSingleton(new Moq.Mock<IDefinitionCompiler>().Object);
        services.AddSingleton(new Moq.Mock<IContractSnapshotCatalog>().Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var lifecycle = Assert.IsType<DefinitionLifecycleService>(scope.ServiceProvider.GetRequiredService<IDefinitionLifecycleService>());

        Assert.False(lifecycle.History.HistoryEnabled);
    }

    /// <summary>The options <see cref="CompilerServiceCollectionExtensions.AddFrontierWorkflowCompiler(IServiceCollection, IConfiguration)"/> binds from <paramref name="settings"/>.</summary>
    private static ChatDesignerHistoryOptions Bound(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var provider = new ServiceCollection().AddFrontierWorkflowCompiler(configuration).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ChatDesignerHistoryOptions>>().Value;
    }
}
