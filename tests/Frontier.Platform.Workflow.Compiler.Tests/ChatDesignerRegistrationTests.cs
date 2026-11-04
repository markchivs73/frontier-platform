using Frontier.Platform.Abstractions;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Frontier.Platform.Workflow.Compiler.Tests;

/// <summary>
/// S13.203: the container builds <see cref="IChatDesignerService"/> from the registration after the
/// merge-service dependency was dropped from its constructor.
/// </summary>
public sealed class ChatDesignerRegistrationTests
{
    [Fact]
    public void AddFrontierWorkflowCompiler_ResolvesTheChatDesigner()
    {
        var services = new ServiceCollection().AddFrontierWorkflowCompiler(new ConfigurationBuilder().Build());
        // The host supplies these, and the compiler needs the host's contract types, so stand in for
        // them; the last registration wins on resolve. What is under test is the factory's wiring.
        AddMock<IDefinitionCompiler>(services);
        AddMock<IDefinitionStore>(services);
        AddMock<IChatClient>(services);
        AddMock<IWorkflowSchemaProvider>(services);
        AddMock<IApproverRoleCatalog>(services);
        AddMock<IDesignerToolCatalog>(services);
        AddMock<IAgentRoleCatalog>(services);
        AddMock<IDesignerModelProvider>(services);
        AddMock<IInstructionCatalog>(services);
        AddMock<IContextComponentCatalog>(services);
        AddMock<IEntryContractCatalog>(services);
        AddMock<IContractSnapshotCatalog>(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ChatDesignerService>(scope.ServiceProvider.GetRequiredService<IChatDesignerService>());
    }

    private static void AddMock<T>(IServiceCollection services)
        where T : class =>
        services.AddSingleton(new Mock<T>().Object);
}
