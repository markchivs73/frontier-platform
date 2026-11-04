using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using static Frontier.Platform.Workflow.Compiler.Tests.ChatDesignerServiceTests;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.202 (K7): the designer call goes to the client the resolved mapping entry routes to, not the
/// deployment's bare registered <see cref="IChatClient"/> — so a selection that names an Azure client
/// reaches that client with its deployment as the model id, and the registered client is not called.
/// A selection with no client still reaches the registered client; that case is the consumer's to route.
/// </summary>
public sealed class DesignerModelRoutingTests
{
    private const string AzureDeployment = "designer-gpt-deployment";

    [Fact]
    public async Task AzureMappedDesignerRole_CallReachesTheAzureClient_WithTheDeploymentAsModelId()
    {
        var registered = StreamingClient();
        var azure = StreamingClient();
        var (service, store) = Build(registered.Object, new RoutedModelProvider(azure.Object));

        await RunTurnAsync(service, store);

        azure.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.Is<ChatOptions?>(o => o!.ModelId == AzureDeployment),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoCall(registered);
    }

    [Fact]
    public async Task SelectionWithoutAClient_KeepsTheRegisteredClient()
    {
        var registered = StreamingClient();
        var (service, store) = Build(registered.Object, new RoutedModelProvider(client: null));

        await RunTurnAsync(service, store);

        registered.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DesignTurn_ResolvesTheSelectionOnce_SoModelIdAndClientComeFromOneEntry()
    {
        var azure = StreamingClient();
        var provider = new RoutedModelProvider(azure.Object);
        var (service, store) = Build(StreamingClient().Object, provider);

        await RunTurnAsync(service, store);

        Assert.Equal(1, provider.Calls);
    }

    private static Mock<IChatClient> StreamingClient()
    {
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() => StreamingResponse("prose reply"));
        return client;
    }

    private static void VerifyNoCall(Mock<IChatClient> client) =>
        client.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Never);

    private static (ChatDesignerService Service, InMemoryDefinitionStore Store) Build(IChatClient registered, IDesignerModelProvider provider)
    {
        var store = new InMemoryDefinitionStore();
        var validator = new DefinitionValidator(Array.Empty<IDefinitionValidationRule>());
        var service = new ChatDesignerService(store,
            registered, new WorkflowSchemaProvider(new PermissiveExecutableNodeTypeCatalog(), TestContractSet.Instance), new FakeApproverRoleCatalog(),
            new FakeDesignerToolCatalog(), new FakeAgentRoleCatalog(), new NodeDiffService(), validator, provider,
            new FakeInstructionCatalog(), new FakeContextComponentCatalog(), new FakeEntryContractCatalog(), new FakeContractSnapshotCatalog());
        return (service, store);
    }

    private static async Task RunTurnAsync(ChatDesignerService service, InMemoryDefinitionStore store)
    {
        await store.SaveDraftAsync("wf-x", CreateMinimalDraft("wf-x"), "no-etag", CancellationToken.None);
        await service.SubmitDesignTurnAsync("wf-x",
            new DesignTurnRequest { DesignerId = "d", Message = "build it" }, CancellationToken.None);
    }

    /// <summary>A consumer provider whose designer role maps to an <c>azure-openai</c> entry: it hands back that entry's client.</summary>
    private sealed class RoutedModelProvider(IChatClient? client) : IDesignerModelProvider
    {
        /// <summary>How many times the designer resolved its model.</summary>
        public int Calls { get; private set; }

        public Task<DesignerModelSelection> GetAsync(string workflowId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DesignerModelSelection
            {
                ModelId = AzureDeployment,
                MaxOutputTokens = 4096,
                AdaptiveThinking = false,
                ChatClient = client,
            });
        }
    }
}
