using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.224 (review-01 GOV-11): <see cref="CosmosDefinitionStore.PersistDesignTurnAsync"/> writes the
/// turn it is given — validated mentions included — on the turn's workflow partition.
/// </summary>
public sealed class CosmosDefinitionStoreDesignTurnTests
{
    [Fact]
    public async Task PersistDesignTurn_UpsertsTheTurnWithItsMentions_OnTheWorkflowPartition()
    {
        var turn = DesignTurnFixture.WithMentions("wf-s13224");
        DesignTurnDocument? written = null;
        PartitionKey? partition = null;
        var response = new Mock<ItemResponse<DesignTurnDocument>>();
        response.SetupGet(r => r.Resource).Returns(() => written!);
        var container = new Mock<Container>(MockBehavior.Strict);
        container.Setup(c => c.UpsertItemAsync(It.IsAny<DesignTurnDocument>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback((DesignTurnDocument item, PartitionKey? pk, ItemRequestOptions _, CancellationToken _) => (written, partition) = (item, pk))
            .ReturnsAsync(response.Object);

        var persisted = await new CosmosDefinitionStore(container.Object).PersistDesignTurnAsync(turn, CancellationToken.None);

        Assert.Equal(turn.Mentions, written!.Mentions);
        Assert.Equal(turn.Mentions, persisted.Mentions);
        Assert.Equal(new PartitionKey("wf-s13224"), partition);
    }
}
