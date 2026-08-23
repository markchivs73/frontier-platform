using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// DI registration for the interpreter. The engine registers its **own** internals — the
/// orchestrators, the activity shells, the pipelines and the consolidator — so none of them has
/// to be public merely to be constructed from outside. What the consumer supplies is the ports.
/// </summary>
public static class OrchestrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the interpreter: <see cref="GraphOrchestrator"/>, <see cref="DispatcherOrchestrator"/>,
    /// the activity shells, the agent and tool pipelines, contract resolution and audit
    /// consolidation, plus the <see cref="OrchestrationOptions"/> binding.
    ///
    /// <para>The consumer's composition root must additionally register: the governance
    /// libraries this composes (<c>AddFrontierContextAssembly</c>, <c>AddFrontierModelRoleConfig</c>,
    /// <c>AddFrontierGuardrails</c>, <c>AddFrontierAudit</c>, <c>AddFrontierHitl</c>,
    /// <c>AddFrontierResilience</c>), and an implementation of every port the engine cannot
    /// supply for itself — <see cref="IAgentInvoker"/>, <see cref="IInstructionsResolver"/>,
    /// <see cref="IMcpToolCatalog"/>, <see cref="IMcpEndpointResolver"/>,
    /// <see cref="IMcpWriteClassifier"/>, <see cref="IExecutionSnapshotReader"/>,
    /// <see cref="IEntryPayloadBuilder"/> and <see cref="IContractTypeSet"/>. Each of those names
    /// a vendor or a deployment fact; the engine asks and does not decide.</para>
    /// </summary>
    public static IServiceCollection AddFrontierWorkflowOrchestration(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OrchestrationOptions>()
            .Bind(configuration.GetSection(OrchestrationOptions.ArtifactName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services
            .AddTransient<GraphOrchestrator>()
            .AddTransient<DispatcherOrchestrator>()
            .AddTransient<AgentTaskActivity>()
            .AddTransient<AssembleContextActivity>()
            .AddTransient<ConsolidateAuditActivity>()
            .AddTransient<RequestApprovalActivity>()
            .AddTransient<EscalateApprovalActivity>()
            .AddTransient<InvokeMcpToolActivity>()
            .AddSingleton<IAuditConsolidator, AuditConsolidator>()
            .AddTransient<IAgentTaskActivityPipeline, AgentTaskActivityPipeline>()
            .AddTransient<IMcpToolInvocationPipeline, McpToolInvocationPipeline>()
            .AddSingleton<AgentInvocationDispatcher>()
            .AddSingleton<IContractTypeRegistry, ContractTypeRegistry>()
            .AddSingleton<IContextContentComposer, ContextContentComposer>();
    }
}
