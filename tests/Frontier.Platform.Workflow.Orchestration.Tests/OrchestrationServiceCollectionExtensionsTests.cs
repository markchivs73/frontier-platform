using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// The engine registers its own internals so none of them has to be public merely to be
/// constructed from outside. These tests pin that: what the consumer must supply is the ports,
/// and nothing else.
/// </summary>
public sealed class OrchestrationServiceCollectionExtensionsTests
{
    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{OrchestrationOptions.ArtifactName}:SandboxMode"] = "false",
        }).Build();

    [Fact]
    public void AddFrontierWorkflowOrchestration_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            OrchestrationServiceCollectionExtensions.AddFrontierWorkflowOrchestration(null!, Configuration()));

    [Fact]
    public void AddFrontierWorkflowOrchestration_NullConfiguration_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddFrontierWorkflowOrchestration(null!));

    [Fact]
    public void AddFrontierWorkflowOrchestration_RegistersTheInterpretersOwnParts()
    {
        var services = new ServiceCollection().AddFrontierWorkflowOrchestration(Configuration());

        var registered = services.Select(d => d.ServiceType).ToHashSet();

        Assert.Contains(typeof(GraphOrchestrator), registered);
        Assert.Contains(typeof(DispatcherOrchestrator), registered);
        Assert.Contains(typeof(AgentTaskActivity), registered);
        Assert.Contains(typeof(ConsolidateAuditActivity), registered);
        Assert.Contains(typeof(IAuditConsolidator), registered);
        Assert.Contains(typeof(IAgentTaskActivityPipeline), registered);
        Assert.Contains(typeof(IContractTypeRegistry), registered);
    }

    [Fact]
    public void AddFrontierWorkflowOrchestration_RegistersNoPortImplementation()
    {
        // The engine must never quietly supply a default for something that names a vendor or a
        // deployment fact — a default here would mean a misconfigured deployment silently runs
        // with the wrong adapter instead of failing to start.
        var services = new ServiceCollection().AddFrontierWorkflowOrchestration(Configuration());

        var registered = services.Select(d => d.ServiceType).ToHashSet();

        Assert.DoesNotContain(typeof(IAgentInvoker), registered);
        Assert.DoesNotContain(typeof(IInstructionsResolver), registered);
        Assert.DoesNotContain(typeof(IMcpToolCatalog), registered);
        Assert.DoesNotContain(typeof(IMcpEndpointResolver), registered);
        Assert.DoesNotContain(typeof(IMcpWriteClassifier), registered);
        Assert.DoesNotContain(typeof(IExecutionSnapshotReader), registered);
        Assert.DoesNotContain(typeof(IEntryPayloadBuilder), registered);
        Assert.DoesNotContain(typeof(IContractTypeSet), registered);
    }

    [Fact]
    public void AddFrontierWorkflowOrchestration_BindsOrchestrationOptions()
    {
        var provider = new ServiceCollection()
            .AddFrontierWorkflowOrchestration(Configuration())
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrchestrationOptions>>().Value);
    }
}
