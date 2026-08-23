using System.Text;
using Frontier.Platform.Abstractions;
using Frontier.Platform.Audit;
using Frontier.Platform.ContextAssembly;
using Frontier.Platform.Guardrails;
using Frontier.Platform.ModelRoleConfig;
using Frontier.Platform.Serialization;
using Frontier.Platform.Workflow.Model;
using Microsoft.Extensions.AI;
using ContextPackageContract = Frontier.Platform.Serialization.ContextPackage;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>S4.2 tests for <see cref="AgentTaskActivityPipeline"/>.</summary>
public sealed class AgentTaskActivityPipelineTests
{
    [Fact]
    public async Task RunAsync_NullInput_ThrowsArgumentNullException()
    {
        var pipeline = BuildPipeline(ScopeSectionFixture(), out _, out _);

        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.RunAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_EntryNodeWithoutUpstreamPayload_ReturnsValidatedOutputFromEngagementBrief()
    {
        var scope = ScopeSectionFixture();
        var pipeline = BuildPipeline(scope, out var invoker, out _, dynamicContent: """{"engagement_brief":"Design a product scope."}""");
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        var result = await pipeline.RunAsync(input, CancellationToken.None);

        var (expectedPayload, expectedHash) = OutputPayloadBuilder.Build(scope, typeof(SummaryArtifact));
        Assert.Equal(input.NodeId, result.NodeId);
        Assert.Equal(input.ArtifactKey, result.ArtifactKey);
        Assert.Equal(nameof(SummaryArtifact), result.OutputContractType);
        Assert.Equal(expectedPayload, result.OutputPayload);
        Assert.Equal(expectedHash, result.OutputHash);
        Assert.Equal("deep-reasoning", result.ResolvedModel.RoleId);
        Assert.Equal("claude-fable-5", result.ResolvedModel.ModelId);
        Assert.Equal(HostBuildInfo.Version, result.HostBuild);   // ADR-E15 D1: every activity result carries the executing build
        Assert.Contains("\"narrative\":\"Design a product scope.\"", invoker.ReceivedRequest!.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NonEntryNodeWithValidUpstreamPayload_ValidatesAgainstInputContractType()
    {
        var scope = ScopeSectionFixture();
        var upstreamPayload = Encoding.UTF8.GetString(CanonicalProfile.SerializeCanonical(scope));
        var approach = new PlanArtifact { Strategy = "strategy", CostEstimate = 100m };
        var pipeline = BuildPipeline(approach, out var invoker, out _);
        var input = BuildInput(nameof(SummaryArtifact), nameof(PlanArtifact), upstreamPayload);

        var result = await pipeline.RunAsync(input, CancellationToken.None);

        var (expectedPayload, expectedHash) = OutputPayloadBuilder.Build(approach, typeof(PlanArtifact));
        Assert.Equal(nameof(PlanArtifact), result.OutputContractType);
        Assert.Equal(expectedPayload, result.OutputPayload);
        Assert.Equal(expectedHash, result.OutputHash);
        Assert.Contains(upstreamPayload, invoker.ReceivedRequest!.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidUpstreamPayload_ThrowsContractViolationException()
    {
        var approach = new PlanArtifact { Strategy = "strategy", CostEstimate = 1m };
        var pipeline = BuildPipeline(approach, out _, out _);
        var input = BuildInput(nameof(SummaryArtifact), nameof(PlanArtifact), upstreamPayload: """{"schema_version":"1.0","title":"","objectives":[]}""");

        var exception = await Assert.ThrowsAsync<ContractViolationException>(() => pipeline.RunAsync(input, CancellationToken.None));

        Assert.Equal(nameof(SummaryArtifact), exception.ContractType);
    }

    [Fact]
    public async Task RunAsync_RecordsAuditTelemetryBeforeReturning()
    {
        var scope = ScopeSectionFixture();
        var usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 45, CachedInputTokenCount = 80 };
        var d = Dependencies(scope, dynamicContent: """{"engagement_brief":"Design a product scope."}""", usage: usage);
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        var result = await pipeline.RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(input.ExecutionId, record.ExecutionId);
        Assert.Equal(input.CorrelationId, record.CorrelationId);
        Assert.Equal(input.NodeId, record.NodeId);
        Assert.Equal(input.ArtifactKey, record.ArtifactKey); // platform audit contract keeps section_key until step 3 (ADR-E3a D5a)
        Assert.Equal(input.Role, record.AgentRole);
        Assert.Equal(input.InputContractType, record.InputContractType);
        Assert.Equal(input.OutputContractType, record.OutputContractType);
        Assert.Equal(result.OutputHash, record.OutputHash);
        Assert.Equal(120, record.InputTokens);
        Assert.Equal(45, record.OutputTokens);
        Assert.Equal(80, record.CacheReadTokens);
        Assert.Equal(0, record.CacheWriteTokens);
        Assert.Equal(0, record.RetryCount);
        Assert.True(record.LatencyMs >= 0);
        Assert.Empty(record.ToolCalls);
    }

    [Fact]
    public async Task RunAsync_InvokerReportsToolCalls_RecordsThemOnTelemetry()
    {
        var scope = ScopeSectionFixture();
        var toolCalls = new[] { new ToolCall { Name = "com.example.crm/tickets/get_new_ticket", InvokedAtUtc = DateTime.UtcNow } };
        var d = Dependencies(scope, dynamicContent: """{"engagement_brief":"Design a product scope."}""", toolCalls: toolCalls);
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        await pipeline.RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(toolCalls, record.ToolCalls);
    }

    [Fact]
    public async Task RunAsync_NoUsageReported_DefaultsTokenCountsToZero()
    {
        var scope = ScopeSectionFixture();
        var d = Dependencies(scope, dynamicContent: """{"engagement_brief":"Design a product scope."}""");
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        await pipeline.RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(0, record.InputTokens);
        Assert.Equal(0, record.OutputTokens);
        Assert.Equal(0, record.CacheReadTokens);
        Assert.Equal(0, record.CacheWriteTokens);
    }

    [Fact]
    public async Task RunAsync_UsageReportsCacheCreationTokens_RecordsCacheWriteTokens()
    {
        var scope = ScopeSectionFixture();
        var usage = new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["cache_creation_input_tokens"] = 25 },
        };
        var d = Dependencies(scope, dynamicContent: """{"engagement_brief":"Design a product scope."}""", usage: usage);
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        await pipeline.RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(25, record.CacheWriteTokens);
    }

    [Fact]
    public async Task AssembleAsync_RevisionNoteProvided_PassedToComposer()
    {
        var d = Dependencies();
        var composer = (FakeContextContentComposer)d.Composer;
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { RevisionNote = "redo scope" };
        var resolved = ResolvedModelFixture();

        await pipeline.AssembleAsync(input, resolved, CancellationToken.None);

        Assert.Equal("redo scope", composer.ReceivedRevisionNote);
    }

    [Fact]
    public async Task InvokeAgentAsync_NodeWithToolRefs_PassesRefsToCatalogAndToolsToInvoker()
    {
        var scope = ScopeSectionFixture();
        var registry = new ContractTypeRegistry(TestContractSet.Instance);
        var invoker = new FakeAgentInvoker(scope);
        var tool = AIFunctionFactory.Create(() => "ticket", "get_new_ticket");
        var toolCatalog = new FakeMcpToolCatalog([tool]);
        var d = Dependencies(scope) with { ToolCatalog = toolCatalog, Dispatcher = new AgentInvocationDispatcher(invoker, registry), ContractTypes = registry };
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { ToolRefs = ["com.example.crm/tickets/get_new_ticket"] };
        var resolved = ResolvedModelFixture();
        var context = Package("{}");

        await pipeline.InvokeAgentAsync(input, resolved, context, "{}", CancellationToken.None);

        Assert.Equal(input.ToolRefs, toolCatalog.ReceivedToolRefs);
        Assert.Same(tool, Assert.Single(invoker.ReceivedRequest!.Tools));
    }

    [Fact]
    public async Task InvokeAgentAsync_NodeWithoutToolRefs_PassesEmptyToolsToInvoker()
    {
        var pipeline = BuildPipeline(ScopeSectionFixture(), out var invoker, out _);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();
        var context = Package("{}");

        await pipeline.InvokeAgentAsync(input, resolved, context, "{}", CancellationToken.None);

        Assert.Empty(invoker.ReceivedRequest!.Tools);
    }

    [Fact]
    public async Task AdmitAsync_DenyDecision_ThrowsBudgetExceededException()
    {
        var decision = new AdmissionDecision(AdmissionResult.Deny, "over budget", null, null);
        var pipeline = BuildPipeline(ScopeSectionFixture(), out _, out _, admissionDecision: decision);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(
            () => pipeline.AdmitAsync(input, resolved, "instructions", "prompt", CancellationToken.None));

        Assert.Equal(Phase1GuardrailPolicyCatalogue.Default.PolicyId, exception.PolicyId);
        Assert.Equal("over budget", exception.Reason);
    }

    [Fact]
    public async Task AdmitAsync_DenyDecisionWithNullReason_ThrowsBudgetExceededExceptionWithEmptyReason()
    {
        var decision = new AdmissionDecision(AdmissionResult.Deny, null, null, null);
        var pipeline = BuildPipeline(ScopeSectionFixture(), out _, out _, admissionDecision: decision);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();

        var exception = await Assert.ThrowsAsync<BudgetExceededException>(
            () => pipeline.AdmitAsync(input, resolved, "instructions", "prompt", CancellationToken.None));

        Assert.Equal(string.Empty, exception.Reason);
    }

    [Fact]
    public async Task AdmitAsync_GrantedMaxOutputTokensSet_ReturnsGrantedValue()
    {
        var decision = new AdmissionDecision(AdmissionResult.ProceedWithWarning, "near budget", 100, null);
        var pipeline = BuildPipeline(ScopeSectionFixture(), out _, out _, admissionDecision: decision);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();

        var granted = await pipeline.AdmitAsync(input, resolved, "instructions", "prompt", CancellationToken.None);

        Assert.Equal(100, granted);
    }

    [Fact]
    public async Task AdmitAsync_ProceedWithoutGrantedTokens_ReturnsEstimateMaxOutputTokens()
    {
        var decision = new AdmissionDecision(AdmissionResult.Proceed, null, null, null);
        var pipeline = BuildPipeline(ScopeSectionFixture(), out _, out var admission, admissionDecision: decision);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();

        var granted = await pipeline.AdmitAsync(input, resolved, "instructions", "prompt", CancellationToken.None);

        Assert.Equal(resolved.Entry.MaxOutputTokens, granted);
        Assert.NotNull(admission.ReceivedEstimate);
    }

    [Fact]
    public void Constructor_NullComposer_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(null!, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullAssembleContext_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, null!, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullContractTypes_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, null!, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullModelResolver_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, null!, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullAdmissionController_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, null!, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullInstructionsResolver_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, null!, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullToolCatalog_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, null!, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullDispatcher_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, null!, d.TelemetryStaging, new FakeEntryPayloadBuilder()));
    }

    [Fact]
    public void Constructor_NullTelemetryStaging_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, null!, new FakeEntryPayloadBuilder()));
    }

    private static SummaryArtifact ScopeSectionFixture() => new() { Title = "Scope", Objectives = ["objective"] };

    private static ResolvedModel ResolvedModelFixture() => new()
    {
        RoleId = "deep-reasoning",
        MappingVersion = 1,
        Provider = "anthropic",
        ModelId = "claude-fable-5",
        ModelVersion = null,
        ChainPosition = 0,
        Entry = new ModelEntry
        {
            Provider = "anthropic",
            ModelId = "claude-fable-5",
            InputCostPer1kGbp = 0.003m,
            OutputCostPer1kGbp = 0.015m,
            CacheReadCostPer1kGbp = 0.0003m,
            ContextWindow = 200_000,
            MaxOutputTokens = 4096,
        },
    };

    private static ContextPackageContract Package(string dynamicContent)
    {
        var baselineTier = new BaselineTier { BaselineVersion = "1.0", Components = new[] { "default" }, Content = "{}" };
        var dynamicTier = new DynamicTier { EngagementId = "eng-test", DynamicEpoch = 0, AssembledFromSnapshotRef = "snap-ref", Content = dynamicContent };
        var realTimeTier = new RealTimeTier { Fetches = new List<RealTimeFetch>(), Content = "{}" };
        var hints = new CacheHint { BreakpointAfterBaseline = 2, BreakpointAfterDynamic = 2 + dynamicContent.Length, BaselineCacheKey = "baseline", DynamicCacheKey = "dynamic" };
        return new ContextPackageContract { Baseline = baselineTier, Dynamic = dynamicTier, RealTime = realTimeTier, Hints = hints };
    }

    private static AgentTaskActivityInput BuildInput(string inputContractType, string outputContractType, string? upstreamPayload) => new()
    {
        NodeId = "gen-scope",
        ArtifactKey = "scope",
        Role = "deep-reasoning",
        InstructionsRef = "gen-scope.md",
        InputContractType = inputContractType,
        OutputContractType = outputContractType,
        CorrelationId = "corr-1",
        EngagementId = "eng-1",
        ContextRequest = new ContextRequest
        {
            EngagementId = "eng-1",
            AgentRole = "deep-reasoning",
            BaselineComponents = ["firm-standards"],
            DynamicFields = ["engagement_brief"],
        },
        UpstreamPayload = upstreamPayload,
        ExecutionId = "exec-1",
    };

    private static AgentTaskActivityPipeline BuildPipeline(
        IVersionedContract outputContract,
        out FakeAgentInvoker invoker,
        out FakeAdmissionController admission,
        string dynamicContent = "{}",
        AdmissionDecision? admissionDecision = null)
    {
        var d = Dependencies(outputContract, dynamicContent, admissionDecision);
        invoker = d.Invoker;
        admission = d.AdmissionControllerDouble;

        return new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder());
    }

    private static PipelineDependencies Dependencies(
        IVersionedContract? outputContract = null,
        string dynamicContent = "{}",
        AdmissionDecision? admissionDecision = null,
        UsageDetails? usage = null,
        IReadOnlyList<ToolCall>? toolCalls = null)
    {
        var registry = new ContractTypeRegistry(TestContractSet.Instance);
        var invoker = new FakeAgentInvoker(outputContract ?? ScopeSectionFixture(), usage, toolCalls);
        var admission = new FakeAdmissionController(admissionDecision ?? new AdmissionDecision(AdmissionResult.Proceed, null, null, null));

        return new PipelineDependencies(
            Composer: new FakeContextContentComposer(new ComposedContext { BaselineContent = "{}", DynamicContent = dynamicContent, RealTimeContent = "{}" }),
            AssembleContext: new AssembleContextActivity(new FakeContextAssembler(Package(dynamicContent))),
            ContractTypes: registry,
            ModelResolver: new FakeModelResolver(ResolvedModelFixture()),
            AdmissionController: admission,
            InstructionsResolver: new FakeInstructionsResolver("instructions"),
            ToolCatalog: new FakeMcpToolCatalog(),
            Dispatcher: new AgentInvocationDispatcher(invoker, registry),
            Invoker: invoker,
            AdmissionControllerDouble: admission,
            TelemetryStaging: new FakeAuditTelemetryStaging());
    }

    private sealed record PipelineDependencies(
        IContextContentComposer Composer,
        AssembleContextActivity AssembleContext,
        IContractTypeRegistry ContractTypes,
        IModelResolver ModelResolver,
        IAdmissionController AdmissionController,
        IInstructionsResolver InstructionsResolver,
        IMcpToolCatalog ToolCatalog,
        AgentInvocationDispatcher Dispatcher,
        FakeAgentInvoker Invoker,
        FakeAdmissionController AdmissionControllerDouble,
        FakeAuditTelemetryStaging TelemetryStaging);
}
