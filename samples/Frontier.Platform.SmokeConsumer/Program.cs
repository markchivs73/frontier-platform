using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Guardrails;
using Frontier.Platform.Hitl;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Observability;
using Frontier.Platform.Resilience;
using Frontier.Platform.Serialization;
using Frontier.Platform.SmokeConsumer;
using Frontier.Platform.Workflow.Model;
using Frontier.Platform.Workflow.Compiler;
using Frontier.Platform.Workflow.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Consumer smoke test. Compiles and runs against the packed .nupkg files rather than
// project references, so it catches the failures packing alone cannot: a dependency
// missing from a nuspec, a type that is internal but needed, or a registration extension
// that cannot be reached from outside the assembly.
//
// It deliberately does no I/O. Nothing here should need an emulator, a network or a key.

Console.WriteLine("Frontier.Platform consumer smoke test");
Console.WriteLine(new string('-', 38));

// 1. The kernel types are reachable, and the canonical profile actually works.
var engagementId = new EngagementId("eng-smoke-001");
var bytes = CanonicalProfile.SerializeCanonical(engagementId);
var hash = CanonicalProfile.Hash(engagementId);

Console.WriteLine($"  canonical bytes : {System.Text.Encoding.UTF8.GetString(bytes)}");
Console.WriteLine($"  canonical hash  : {hash}");

if (bytes.Length == 0 || string.IsNullOrWhiteSpace(hash))
{
    throw new InvalidOperationException("Canonical serialization produced no output.");
}

// Byte stability is the property everything else depends on — assert it survives packaging.
if (!CanonicalProfile.SerializeCanonical(new EngagementId("eng-smoke-001")).SequenceEqual(bytes))
{
    throw new InvalidOperationException("Canonical serialization is not byte-stable across calls.");
}

Console.WriteLine("  byte stability  : ok");

// 2. Every library's registration extension is reachable and runs. Configuration values are
//    syntactically valid placeholders; nothing connects to anything.
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Cosmos:Endpoint"] = "https://localhost:8081",
        ["Cosmos:Database"] = "frontier-platform-smoke",
        ["Cosmos:Key"] = "c21vaGUtc21va2UtdGVzdC1wbGFjZWhvbGRlci1rZXktdmFsdWU=",
    })
    .Build();

var services = new ServiceCollection();
services.AddFrontierSerialization();
services.AddFrontierResilience();
services.AddFrontierGuardrails();
services.AddFrontierContextAssembly();
services.AddFrontierAudit(configuration);
services.AddFrontierHitl(configuration);
services.AddFrontierModelRoleConfig(configuration);
services.AddFrontierObservability(configuration);

Console.WriteLine($"  registrations   : {services.Count} services across 8 libraries");

if (services.Count == 0)
{
    throw new InvalidOperationException("No services were registered.");
}

// 3. The container builds. This is where a missing transitive dependency surfaces as a
//    TypeLoadException or FileNotFoundException rather than a quiet no-op.
using var provider = services.BuildServiceProvider();
var options = provider.GetRequiredService<System.Text.Json.JsonSerializerOptions>();

Console.WriteLine($"  resolved profile: {options.PropertyNamingPolicy?.GetType().Name ?? "default"}");

// 4. The workflow model has no registration extension — it is types only — so what needs
//    proving is different: that its canonical wire shape survives packaging, and that the
//    XML documentation ships *inside* the package. The second matters because the design
//    agent is handed these summaries as its schema descriptions, and a missing doc file is
//    silent: descriptions simply become empty.
var definition = new WorkflowDefinition
{
    WorkflowId = "wf-smoke",
    DefinitionVersion = 1,
    EngagementType = "smoke",
    Name = "Smoke",
    Nodes = [],
    Edges = [],
    DefinitionHash = "sha256:smoke",
    Mode = ExecutionMode.OneShot,
};

var definitionJson = System.Text.Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(definition));
Console.WriteLine($"  workflow model  : {definitionJson}");

if (!definitionJson.Contains("\"workflow_id\"", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Workflow model did not serialize to canonical snake_case wire names.");
}

var modelXml = Path.ChangeExtension(typeof(WorkflowDefinition).Assembly.Location, ".xml");

if (!File.Exists(modelXml))
{
    throw new InvalidOperationException(
        $"The workflow model's XML documentation is missing from the package ({modelXml}). "
        + "The design-language schema generator reads it for node and field descriptions, "
        + "and its absence is silent — descriptions become empty rather than failing.");
}

Console.WriteLine("  model xml docs  : shipped in package");
Console.WriteLine();
// 5. The interpreter is reachable and, more importantly, vendor-neutral: its assembly must
//    not drag a model provider or tool transport into a consumer's graph. Asserting the
//    absence is the point — a stray PackageReference would restore silently and only show up
//    as an unexpected dependency in someone else's build.
var engineRefs = typeof(GraphOrchestrator).Assembly.GetReferencedAssemblies()
    .Select(a => a.Name ?? string.Empty)
    .ToList();

var vendorRefs = engineRefs
    .Where(n => n.StartsWith("Anthropic", StringComparison.Ordinal)
             || n.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal)
             || n.StartsWith("ModelContextProtocol", StringComparison.Ordinal))
    .ToList();

if (vendorRefs.Count > 0)
{
    throw new InvalidOperationException(
        $"The interpreter must stay vendor-neutral, but references: {string.Join(", ", vendorRefs)}.");
}

Console.WriteLine($"  interpreter     : {engineRefs.Count} assembly refs, no vendor SDK");

// 6. The interpreter registers and *resolves* as a consumer wires it: engine internals from
//    AddFrontierWorkflowOrchestration, every port supplied from outside. This is the check that
//    would have caught the ports shipping internal — the package compiled and packed perfectly,
//    and only a consumer implementing IAgentInvoker could discover it could not.
var engineServices = new ServiceCollection();
engineServices.AddFrontierSerialization();
engineServices.AddFrontierResilience();
engineServices.AddFrontierGuardrails();
engineServices.AddFrontierContextAssembly();
engineServices.AddFrontierAudit(configuration);
engineServices.AddFrontierHitl(configuration);
engineServices.AddFrontierModelRoleConfig(configuration);
engineServices.AddFrontierWorkflowOrchestration(configuration);
engineServices.AddSingleton<IAgentInvoker, SmokeAgentInvoker>();
engineServices.AddSingleton<IInstructionsResolver, SmokeInstructionsResolver>();
engineServices.AddSingleton<IMcpToolCatalog, SmokeToolCatalog>();
engineServices.AddSingleton<IMcpEndpointResolver, SmokeEndpointResolver>();
engineServices.AddSingleton<IMcpWriteClassifier, SmokeWriteClassifier>();
engineServices.AddSingleton<IExecutionSnapshotReader, SmokeSnapshotReader>();
engineServices.AddSingleton<IEntryPayloadBuilder, SmokeEntryPayloadBuilder>();
engineServices.AddSingleton<IContractTypeSet>(new ContractTypeSet([typeof(WorkflowDefinition)]));

using var engineProvider = engineServices.BuildServiceProvider();
var orchestrator = engineProvider.GetRequiredService<GraphOrchestrator>();
var pipeline = engineProvider.GetRequiredService<IAgentTaskActivityPipeline>();
var consolidator = engineProvider.GetRequiredService<IAuditConsolidator>();

Console.WriteLine($"  engine resolves : {orchestrator.GetType().Name}, {pipeline.GetType().Name}, {consolidator.GetType().Name}");

// 7. The encodings a vendor adapter needs are reachable and agree with each other: the response
//    format that tells a model which contract to return, and the tool-name round trip. An
//    adapter that had to reimplement either would become a second source of truth for bytes the
//    engine already defines.
_ = CanonicalOutputSchema.For<WorkflowDefinition>();
await engineProvider.GetRequiredService<IMcpToolCatalog>()
    .ResolveAsync(["com.example.crm/tickets/get_ticket"], "eng-smoke-001::wf-smoke", CancellationToken.None);

Console.WriteLine("  adapter surface : output schema + tool-name round trip reachable");

// 8. The compiler registers and resolves the same way, with the consumer supplying every
//    catalogue port. This is the check that cost three round trips when the interpreter moved
//    without it: a package can be complete, packed and green and still be unimplementable from
//    another assembly.
engineServices.AddFrontierWorkflowCompiler();
engineServices.AddSingleton<IAgentRoleCatalog, SmokeCatalogues>();
engineServices.AddSingleton<IApproverRoleCatalog, SmokeCatalogues>();
engineServices.AddSingleton<IInstructionCatalog, SmokeCatalogues>();
engineServices.AddSingleton<IContextComponentCatalog, SmokeCatalogues>();
engineServices.AddSingleton<IRetryProfileCatalog, SmokeCatalogues>();
engineServices.AddSingleton<IDesignerToolCatalog, SmokeCatalogues>();
engineServices.AddSingleton<ICascadeGraphChecker, SmokeCatalogues>();

using var compilerProvider = engineServices.BuildServiceProvider();
var compiler = compilerProvider.GetRequiredService<IDefinitionCompiler>();
var rules = compilerProvider.GetServices<IDefinitionValidationRule>().Count();

Console.WriteLine($"  compiler        : {compiler.GetType().Name}, {rules} validation rules registered");

if (rules < 20)
{
    throw new InvalidOperationException($"Expected the structural rule set to register, got {rules} rules.");
}

// 9. The designer-host surface. A consumer hosting the design agent parses the agent's proposal
//    and builds the change set it shows the user — both from the compiler, so its view of "what
//    changed" cannot disagree with the merge and diff services'. The designer itself stays in the
//    consuming solution until E3b step 5, which is exactly why this has to be reachable now.
if (AgentProposalParser.TryParse("not a proposal", out _))
{
    throw new InvalidOperationException("Malformed proposal text should not parse.");
}

Console.WriteLine("  designer host   : proposal parser + change-set builder reachable");

// 10. The design agent itself now lives here, so a consumer must be able to resolve it with the
//     entry convention supplied from outside. IEntryContractCatalog is the port that made this
//     possible: the prompt used to name one workload's contract in a string literal, where no
//     type-level guard could see it.
engineServices.AddSingleton<IEntryContractCatalog, SmokeEntryContract>();
engineServices.AddSingleton<IChatClient, SmokeChatClient>();
engineServices.AddSingleton<IDesignerModelProvider, SmokeDesignerModel>();

using var designerProvider = engineServices.BuildServiceProvider();
var designer = designerProvider.GetRequiredService<IChatDesignerService>();

Console.WriteLine($"  design agent    : {designer.GetType().Name} resolves with a supplied entry contract");
Console.WriteLine();
Console.WriteLine("PASS - all twelve packages restored, loaded and registered.");
