using System.Diagnostics.CodeAnalysis;
using Frontier.Platform.Workflow.Compiler.Rules;
using Frontier.Platform.Workflow.Compiler.Schema;
using Frontier.Platform.Workflow.Compiler.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frontier.Platform.Workflow.Compiler;

/// <summary>
/// The compiler registers its own internals — the validator, the structural rule set, schema
/// generation and the publish lifecycle — so none of them is public merely to be constructed
/// from outside. The consumer supplies the catalogue ports and the design agent.
///
/// DI registration for the Definition Compiler (doc 13, S8.2a/b).
/// Phase A: Registers the compiler service and all Phase 1 compiler-owned validation rules.
/// Phase B: Registers the lifecycle service, definition store, and publish governance config.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "DI registration: only configuration, no testable logic")]
public static class CompilerServiceCollectionExtensions
{
    public static IServiceCollection AddFrontierWorkflowCompiler(this IServiceCollection services)
    {
        // Register compiler-owned pure-tier rules (Phase 1 catalogue per C-8)
        services.AddSingleton<IDefinitionValidationRule, StructureRequiredFieldsRule>();
        services.AddSingleton<IDefinitionValidationRule, StructureDispatcherInputRule>();
        services.AddSingleton<IDefinitionValidationRule, StructureUniqueNodeIdsRule>();
        services.AddSingleton<IDefinitionValidationRule, GraphSingleEntryReachableRule>();
        services.AddSingleton<IDefinitionValidationRule, GraphIsAcyclicRule>();
        services.AddSingleton<IDefinitionValidationRule, GraphFanOutFanInRule>();
        services.AddSingleton<IDefinitionValidationRule, NodeTypeSupportedRule>();
        services.AddSingleton<IDefinitionValidationRule, OutputContractBindableRule>();
        services.AddSingleton<IDefinitionValidationRule, GraphDecisionEdgesRule>();
        services.AddSingleton<IDefinitionValidationRule, DataSingleDataPredecessorRule>();
        services.AddSingleton<IDefinitionValidationRule, DataSchemaRefMatchRule>();
        services.AddSingleton<IDefinitionValidationRule, AgentOutputNotEnvelopeRule>();
        services.TryAddSingleton(new PublishGovernanceConfig()); // ADR-DC1 default: self-approval allowed; Host overrides from deployment config (S13.7g)
        services.AddSingleton<IDefinitionValidationRule, VersioningNoClashRule>();
        services.AddSingleton<IDefinitionValidationRule, ModelIdRejectionRule>();

        // S9.30: remaining pure-tier catalogue rows (doc 13 §4.2). cascade.acyclic delegates to
        // Cascade Logic's ValidateAtPublish guardian through the consumer-owned
        // ICascadeGraphChecker seam — the adapter is wired by the Host composition root
        // (library-boundaries: no direct CascadeLogic reference from this library).
        services.AddSingleton<IDefinitionValidationRule, CascadeAcyclicRule>();
        services.AddSingleton<IDefinitionValidationRule, ContextBaselineScopedRule>();
        services.AddSingleton<IDefinitionValidationRule, McpWriteIdempotencyRule>();
        services.AddSingleton<IDefinitionValidationRule, HitlRollbackTargetValidRule>();
        services.AddSingleton<IDefinitionValidationRule, ResilienceOverridesTightenOnlyRule>();

        // S9.27c: first two resourced-tier rules (doc 13 §4.2), pulled ahead of the full S9.30
        // rollout (C-21b) so the first real publish (S9.28) is resource-verified, not just
        // structurally valid. Constructed via the same IDesignerToolCatalog/IAgentRoleCatalog
        // the chat designer's system prompt already uses (S9.26/S9.27) — registered Scoped by
        // the Host composition root, so these rules must be Scoped too (a Singleton can't
        // capture a Scoped dependency; ValidateScopes catches this at container-build time).
        services.AddScoped<IDefinitionValidationRule, McpToolResolvesRule>();
        services.AddScoped<IDefinitionValidationRule, ModelRoleRolesExistRule>();

        // S9.30: remaining resourced-tier catalogue rows (doc 13 §4.2). Compiler-owned catalogs
        // (reflection contract registry, retention default) register here; consumer-owned
        // catalogs (IContextComponentCatalog, IInstructionCatalog, IRetryProfileCatalog) are
        // adapted and wired by the Host composition root, S9.27c pattern.
        services.AddSingleton<IContractTypeCatalog, ReflectionContractTypeCatalog>();
        // S13.7h (ADR-DC7): permissive default so a compiler composed without a runtime behaves as
        // it did before; the Host replaces it with the orchestrator's own capability list.
        // S13.25: fail closed. The default is what the interpreter actually executes, not
        // everything — a missed registration must not silently reopen the S13.7h hole.
        services.AddSingleton<IExecutableNodeTypeCatalog, OrchestratorExecutableNodeTypeCatalog>();
        services.TryAddSingleton(new RetentionWindowConfig());

        // S9.43 (doc 19 §A4-R2/C-31): A4 "Expected shape" panel + "Use example" prefill.
        // Stateless over the singleton contract catalog above.
        services.AddSingleton<ITestRunInputSchemaProvider, TestRunInputSchemaProvider>();
        services.AddScoped<IDefinitionValidationRule, DataContractTypesResolveRule>();
        services.AddScoped<IDefinitionValidationRule, DataEdgeTypeMatchRule>();
        services.AddScoped<IDefinitionValidationRule, ContextKnownComponentsRule>();
        services.AddScoped<IDefinitionValidationRule, AgentInstructionsResolveRule>();
        services.AddScoped<IDefinitionValidationRule, HitlApproverRolesExistRule>();
        services.AddScoped<IDefinitionValidationRule, ResilienceProfileExistsRule>();
        services.AddScoped<IDefinitionValidationRule, TimeoutsNestingRule>();
        services.AddScoped<IDefinitionValidationRule, RetentionFitsWindowRule>();
        services.AddScoped<IDefinitionValidationRule, DeterminismPredicatesCompileRule>();

        // S9.30: Runtime-tier catalogue row — executes only in the sandbox test-run channel
        // (S9.38 wires it); ValidateAsync filters to Pure + Resourced.

        // S9.7: Register the workflow design-language schema provider (doc 14 §7, ADR-CD3).
        // Singleton: the schema is generated once from the loaded Abstractions assembly and cached.
        services.AddSingleton<IWorkflowSchemaProvider, WorkflowSchemaProvider>();

        // Register the compiler service (aggregates all IDefinitionValidationRule implementations).
        // Scoped, not Singleton (S9.27c): resourced-tier rules depend on Scoped catalogs
        // (IDesignerToolCatalog/IAgentRoleCatalog), and a Singleton can't capture a Scoped
        // dependency. Every existing consumer (IDefinitionLifecycleService, ITestRunService,
        // IProposalMergeService, IChatDesignerService) is already Scoped, so this has no
        // captive-dependency knock-on effect.
        services.AddScoped<IDefinitionCompiler>(sp =>
        {
            var rules = sp.GetRequiredService<IEnumerable<IDefinitionValidationRule>>();
            return new DefinitionValidator(rules);
        });

        // Phase B: Register definition store and lifecycle service
        services.AddScoped<IDefinitionStore>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var container = cosmosClient.GetDatabase("frontier-workflow").GetContainer("workflow-definitions");
            return new CosmosDefinitionStore(container);
        });

        services.AddScoped<IDefinitionLifecycleService>(sp =>
        {
            var store = sp.GetRequiredService<IDefinitionStore>();
            var compiler = sp.GetRequiredService<IDefinitionCompiler>();
            var governance = sp.GetService<PublishGovernanceConfig>() ?? new PublishGovernanceConfig();
            return new DefinitionLifecycleService(store, compiler, governance);
        });

        // Phase D/S8.3: Register sandbox test-run service. S9.38a: runs the draft on the real
        // GraphOrchestrator machinery via the Host-adapted ITestRunExecutor seam (registered by
        // the composition root, S9.30 pattern) instead of simulating outcomes.
        services.AddScoped<ITestRunService>(sp =>
        {
            var store = sp.GetRequiredService<IDefinitionStore>();
            var compiler = sp.GetRequiredService<IDefinitionCompiler>();
            var executor = sp.GetRequiredService<ITestRunExecutor>();
            var telemetry = sp.GetRequiredService<ITestRunTelemetryReader>();
            var sections = sp.GetRequiredService<ITestRunArtifactReader>();
            return new TestRunService(store, compiler, executor, telemetry, sections);
        });

        // Phase D: Register retirement monitor (evidence-based candidate detection)
        services.AddScoped<IRetirementMonitor>(sp =>
        {
            var cosmosClient = sp.GetRequiredService<CosmosClient>();
            var container = cosmosClient.GetDatabase("frontier-workflow").GetContainer("execution-snapshots");
            return new RetirementMonitor(container);
        });

        // S8.4: Register chat designer services (persistent draft-scoped conversation)
        services.AddScoped<INodeDiffService, NodeDiffService>();
        services.AddScoped<IProposalMergeService>(sp =>
        {
            var store = sp.GetRequiredService<IDefinitionStore>();
            var compiler = sp.GetRequiredService<IDefinitionCompiler>();
            var diffService = sp.GetRequiredService<INodeDiffService>();
            return new ProposalMergeService(store, compiler, diffService);
        });

        services.AddScoped<IChatDesignerService>(sp =>
        {
            var store = sp.GetRequiredService<IDefinitionStore>();
            var mergeService = sp.GetRequiredService<IProposalMergeService>();
            var chatClient = sp.GetRequiredService<IChatClient>();
            var schemaProvider = sp.GetRequiredService<IWorkflowSchemaProvider>();
            var roleCatalog = sp.GetRequiredService<IApproverRoleCatalog>();
            var toolCatalog = sp.GetRequiredService<IDesignerToolCatalog>();
            var agentRoleCatalog = sp.GetRequiredService<IAgentRoleCatalog>();
            var diffService = sp.GetRequiredService<INodeDiffService>();
            var compiler = sp.GetRequiredService<IDefinitionCompiler>();
            var modelProvider = sp.GetRequiredService<IDesignerModelProvider>();
            var instructionCatalog = sp.GetRequiredService<IInstructionCatalog>();
            var componentCatalog = sp.GetRequiredService<IContextComponentCatalog>();
            var entryContractCatalog = sp.GetRequiredService<IEntryContractCatalog>();
            return new ChatDesignerService(store, mergeService, chatClient, schemaProvider, roleCatalog, toolCatalog, agentRoleCatalog, diffService, compiler, modelProvider, instructionCatalog, componentCatalog, entryContractCatalog);
        });

        return services;
    }
}
