using System.Security.Cryptography;
using System.Text;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Guardrails;
using Frontier.Platform.ModelRoleConfig;
using Microsoft.Extensions.AI;
using ContextPackageContract = Frontier.Platform.Serialization.ContextPackage;
using Frontier.Platform.Serialization;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The full <c>InvokeAgentActivity</c> pipeline (doc 00 §4.3 step 5, S4.2): assemble
/// context, validate the input contract, resolve the agent's model (Model-Role Config),
/// check Guardrails admission, invoke MAF (ADR-AG1 direct-POCO binding), then validate
/// the output contract. Model-Role resolution runs <em>before</em> context assembly —
/// doc 00 §4.3 orders them the other way round, but <see cref="CachingMetadata"/> (which
/// <see cref="AssembleContextActivity"/> requires) needs the resolved model's provider/id
/// for cache-strategy selection, and resolution has no dependency on assembled context.
/// </summary>
internal sealed class AgentTaskActivityPipeline : IAgentTaskActivityPipeline
{
    private readonly IContextContentComposer composer;
    private readonly AssembleContextActivity assembleContext;
    private readonly IContractTypeRegistry contractTypes;
    private readonly IModelResolver modelResolver;
    private readonly IAdmissionController admissionController;
    private readonly IInstructionsResolver instructionsResolver;
    private readonly IMcpToolCatalog toolCatalog;
    private readonly AgentInvocationDispatcher dispatcher;
    private readonly IAuditTelemetryStaging telemetryStaging;
    private readonly IEntryPayloadBuilder entryPayloads;

    /// <summary>Constructs the pipeline over its per-step collaborators.</summary>
    public AgentTaskActivityPipeline(
        IContextContentComposer composer,
        AssembleContextActivity assembleContext,
        IContractTypeRegistry contractTypes,
        IModelResolver modelResolver,
        IAdmissionController admissionController,
        IInstructionsResolver instructionsResolver,
        IMcpToolCatalog toolCatalog,
        AgentInvocationDispatcher dispatcher,
        IAuditTelemetryStaging telemetryStaging,
        IEntryPayloadBuilder entryPayloads)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(assembleContext);
        ArgumentNullException.ThrowIfNull(contractTypes);
        ArgumentNullException.ThrowIfNull(modelResolver);
        ArgumentNullException.ThrowIfNull(admissionController);
        ArgumentNullException.ThrowIfNull(instructionsResolver);
        ArgumentNullException.ThrowIfNull(toolCatalog);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(telemetryStaging);
        ArgumentNullException.ThrowIfNull(entryPayloads);

        this.composer = composer;
        this.assembleContext = assembleContext;
        this.contractTypes = contractTypes;
        this.modelResolver = modelResolver;
        this.admissionController = admissionController;
        this.instructionsResolver = instructionsResolver;
        this.toolCatalog = toolCatalog;
        this.dispatcher = dispatcher;
        this.telemetryStaging = telemetryStaging;
        this.entryPayloads = entryPayloads;
    }

    /// <inheritdoc />
    public async Task<AgentTaskActivityResult> RunAsync(AgentTaskActivityInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var resolved = await modelResolver.ResolveAsync(BuildResolutionRequest(input), ct).ConfigureAwait(false);
        var context = await AssembleAsync(input, resolved, ct).ConfigureAwait(false);
        var inputPayload = BuildInputPayload(input, context, entryPayloads);
        contractTypes.DeserializeAndValidate(input.InputContractType, inputPayload);

        var invocation = await InvokeAgentAsync(input, resolved, context, inputPayload, ct).ConfigureAwait(false);
        var outputType = contractTypes.Resolve(input.OutputContractType);
        var (payload, hash) = OutputPayloadBuilder.Build(invocation.Result, outputType);

        var telemetry = BuildTelemetryRecord(input, resolved, invocation, inputPayload, hash);
        await telemetryStaging.RecordInvocationAsync(telemetry, ct).ConfigureAwait(false);

        return BuildResult(input, resolved, payload, hash);
    }

    /// <summary>Builds the Model-Role Config resolution request for <paramref name="input"/>'s role (doc 08 §5).</summary>
    internal static ResolutionRequest BuildResolutionRequest(AgentTaskActivityInput input) => new()
    {
        RoleId = input.Role,
        EngagementId = input.EngagementId,
        MappingVersion = null,
    };

    /// <summary>Composes and assembles the three-tier context package for <paramref name="input"/>, using <paramref name="resolved"/> for cache-strategy metadata.</summary>
    internal async Task<ContextPackageContract> AssembleAsync(AgentTaskActivityInput input, ResolvedModel resolved, CancellationToken ct)
    {
        var composed = await composer.ComposeAsync(input.ContextRequest, input.RevisionNote, ct).ConfigureAwait(false);
        var metadata = new CachingMetadata(resolved.Provider, resolved.ModelId, resolved.ModelVersion, resolved.Entry.ContextWindow, DateTime.UtcNow);
        var request = new AssembleContextRequest(metadata, composed.BaselineContent, composed.DynamicContent, composed.RealTimeContent);

        return await assembleContext.RunAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <paramref name="input"/>'s upstream Data-edge payload, or — for the entry
    /// node (<see cref="AgentTaskActivityInput.UpstreamPayload"/> is <see langword="null"/>)
    /// — the payload the workload's <see cref="IEntryPayloadBuilder"/> derives from
    /// <paramref name="context"/>'s dynamic tier (S4.1; the mapping is workload vocabulary,
    /// so it lives behind the port — S13.12c).
    /// </summary>
    internal static string BuildInputPayload(AgentTaskActivityInput input, ContextPackageContract context, IEntryPayloadBuilder entryPayloads) =>
        input.UpstreamPayload ?? entryPayloads.BuildEntryPayload(context.Dynamic.Content);

    /// <summary>Resolves instructions and tools, builds the prompt, checks Guardrails admission, and runs the MAF invocation (doc 00 §4.3 steps 5b–5e).</summary>
    internal async Task<AgentInvocationResult> InvokeAgentAsync(AgentTaskActivityInput input, ResolvedModel resolved, ContextPackageContract context, string inputPayload, CancellationToken ct)
    {
        var instructions = await instructionsResolver.ResolveAsync(input.InstructionsRef, ct).ConfigureAwait(false);
        var prompt = PromptBuilder.Build(context, input.InputContractType, inputPayload);
        var maxOutputTokens = await AdmitAsync(input, resolved, instructions, prompt, ct).ConfigureAwait(false);
        var tools = await toolCatalog.ResolveAsync(input.ToolRefs, input.ExecutionId, ct).ConfigureAwait(false);

        var request = new AgentInvocationRequest
        {
            Instructions = instructions,
            Prompt = prompt,
            ModelId = resolved.ModelId,
            MaxOutputTokens = maxOutputTokens,
            Tools = tools,
        };

        return await dispatcher.InvokeAsync(input.OutputContractType, request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks Guardrails admission for this invocation (doc 07 §5) and returns the granted
    /// output-token budget. Throws <see cref="BudgetExceededException"/> (permanent, doc
    /// 10 §3) if <see cref="AdmissionResult.Deny"/>.
    /// </summary>
    internal async Task<long> AdmitAsync(AgentTaskActivityInput input, ResolvedModel resolved, string instructions, string prompt, CancellationToken ct)
    {
        var estimate = BuildCostEstimate(input, resolved, instructions, prompt);
        var decision = await admissionController.AdmitAsync(estimate, ct).ConfigureAwait(false);

        if (decision.Result == AdmissionResult.Deny)
        {
            throw new BudgetExceededException(Phase1GuardrailPolicyCatalogue.Default.PolicyId, decision.Reason ?? string.Empty);
        }

        return decision.GrantedMaxOutputTokens ?? estimate.MaxOutputTokens;
    }

    /// <summary>Builds the pre-call cost estimate (doc 07 §4) from a character-based prompt-token heuristic and <paramref name="resolved"/>'s cost metadata.</summary>
    internal static InvocationCostEstimate BuildCostEstimate(AgentTaskActivityInput input, ResolvedModel resolved, string instructions, string prompt)
    {
        var promptTokens = EstimatePromptTokens(instructions, prompt);
        var maxOutputTokens = resolved.Entry.MaxOutputTokens;

        return new InvocationCostEstimate(
            input.CorrelationId,
            input.ExecutionId,
            input.EngagementId,
            input.NodeId,
            input.Role,
            resolved.ModelId,
            promptTokens,
            maxOutputTokens,
            EstimateCostGbp(resolved.Entry, promptTokens, maxOutputTokens));
    }

    /// <summary>PoC-grade prompt-token heuristic: ~4 characters per token (S4.2; M.E.AI's reported <see cref="Microsoft.Extensions.AI.UsageDetails"/> replaces this post-invocation).</summary>
    internal static long EstimatePromptTokens(string instructions, string prompt) =>
        (long)Math.Ceiling((instructions.Length + prompt.Length) / 4.0);

    /// <summary>Estimates the invocation's worst-case cost in GBP from <paramref name="entry"/>'s per-1k-token rates (doc 08 §6, scale 4).</summary>
    internal static decimal EstimateCostGbp(ModelEntry entry, long promptTokens, long maxOutputTokens) =>
        Math.Round((promptTokens / 1000m * entry.InputCostPer1kGbp) + (maxOutputTokens / 1000m * entry.OutputCostPer1kGbp), 4);

    /// <summary>Builds the activity's result, projecting <paramref name="resolved"/> into the audit-facing <see cref="ResolvedModelSummary"/> (doc 08 §6).</summary>
    internal static AgentTaskActivityResult BuildResult(AgentTaskActivityInput input, ResolvedModel resolved, string payload, string hash) => new()
    {
        NodeId = input.NodeId,
        ArtifactKey = input.ArtifactKey,
        OutputContractType = input.OutputContractType,
        OutputPayload = payload,
        OutputHash = hash,
        ResolvedModel = ToSummary(resolved),
        HostBuild = HostBuildInfo.Version,
    };

    /// <summary>Projects a <see cref="ResolvedModel"/> to its audit-facing <see cref="ResolvedModelSummary"/> (doc 08 §6).</summary>
    internal static ResolvedModelSummary ToSummary(ResolvedModel resolved) => new()
    {
        RoleId = resolved.RoleId,
        Provider = resolved.Provider,
        ModelId = resolved.ModelId,
        ModelVersion = resolved.ModelVersion,
        ChainPosition = resolved.ChainPosition,
        MappingVersion = resolved.MappingVersion,
    };

    /// <summary>
    /// Builds the per-invocation <see cref="AuditTelemetryRecord"/> (C-14) for staging:
    /// <see cref="AuditTelemetryRecord.RetryCount"/> is always 0 (no retry tracking exists
    /// at this layer, mirroring <c>GraphOrchestratorSteps.BuildStepCompletion</c>),
    /// <see cref="AuditTelemetryRecord.ToolCalls"/> is <paramref name="invocation"/>'s MCP
    /// tool calls (ADR-CD6, S9.25 — <c>[]</c> when the node declared no tools or none were
    /// called), and the per-tier cache-changed flags use C-15's S5.3 placeholder (baseline
    /// unchanged, dynamic/real-time changed) pending the QG-5 verdict.
    /// </summary>
    internal static AuditTelemetryRecord BuildTelemetryRecord(AgentTaskActivityInput input, ResolvedModel resolved, AgentInvocationResult invocation, string inputPayload, string outputHash) => new()
    {
        ExecutionId = input.ExecutionId,
        CorrelationId = input.CorrelationId,
        NodeId = input.NodeId,
        ArtifactKey = input.ArtifactKey,
        AgentRole = input.Role,
        ResolvedModel = ToSummary(resolved),
        InputContractType = input.InputContractType,
        InputHash = ComputeInputHash(inputPayload),
        OutputContractType = input.OutputContractType,
        OutputHash = outputHash,
        InputTokens = invocation.Usage?.InputTokenCount ?? 0,
        OutputTokens = invocation.Usage?.OutputTokenCount ?? 0,
        CacheReadTokens = invocation.Usage?.CachedInputTokenCount ?? 0,
        CacheWriteTokens = ExtractCacheWriteTokens(invocation.Usage),
        RetryCount = 0,
        LatencyMs = invocation.LatencyMs,
        ToolCalls = invocation.ToolCalls,
        BaselineCacheChanged = false,
        DynamicCacheChanged = true,
        RealTimeCacheChanged = true,
        InvokedAtUtc = DateTime.UtcNow,
    };

    /// <summary>SHA256 hex hash of <paramref name="inputPayload"/>'s canonical UTF8 bytes, for <see cref="AuditTelemetryRecord.InputHash"/>.</summary>
    internal static string ComputeInputHash(string inputPayload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputPayload)));

    /// <summary>
    /// Reads cache-creation (cache-write) tokens from <paramref name="usage"/>'s
    /// provider-specific <see cref="UsageDetails.AdditionalCounts"/> (C-15) — the current
    /// Anthropic provider/SDK does not surface this, so this is forward-compatible and
    /// defaults to 0.
    /// </summary>
    internal static long ExtractCacheWriteTokens(UsageDetails? usage) =>
        usage?.AdditionalCounts?.TryGetValue("cache_creation_input_tokens", out var tokens) == true ? tokens : 0;
}
