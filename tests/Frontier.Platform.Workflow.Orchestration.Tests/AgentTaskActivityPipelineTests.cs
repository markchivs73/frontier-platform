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
    public void ComputeInputHash_SameUpstreamPayloadInTwoKeyOrders_IsIdentical()
    {
        var first = AgentTaskActivityPipeline.ComputeInputHash("""{"status":"open","ticket":{"id":"T-1","rev":2}}""");
        var second = AgentTaskActivityPipeline.ComputeInputHash("""{"ticket":{"rev":2,"id":"T-1"},"status":"open"}""");

        Assert.Equal(first, second);
        Assert.Equal(JsonCanonicalizer.Hash("""{"status":"open","ticket":{"id":"T-1","rev":2}}"""u8), first);
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
        Assert.Equal(TestHostBuildStamp.Value, result.HostBuild);   // ADR-E15 D1, S13.182: every activity result carries the worker build the port supplies
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
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
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
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
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
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
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
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        await pipeline.RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(25, record.CacheWriteTokens);
    }

    [Fact]
    public async Task AssembleAsync_PinnedEpochOnInput_PassedToComposer()
    {
        // S13.60: the activity reads the epoch the run is pinned to, never ":current".
        var d = Dependencies();
        var composer = (FakeContextContentComposer)d.Composer;
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { DynamicContextEpoch = 3 };
        var resolved = ResolvedModelFixture();
        await pipeline.AssembleAsync(input, resolved, CancellationToken.None);
        Assert.Equal(3, composer.ReceivedPinnedEpoch);
    }

    [Fact]
    public async Task AssembleAsync_ComposerReportsEpoch_ProvenanceReachesTheAssembler()
    {
        // S13.60 (doc 04 §4 step 3): the tier cites the engagement, epoch and epoch document the composer actually read.
        var assembler = new FakeContextAssembler(Package("{}"));
        var d = Dependencies() with
        {
            Composer = new FakeContextContentComposer(new ComposedContext { BaselineContent = "{}", BaselineProvenance = BaselineProvenanceFixture, DynamicContent = "{}", RealTimeContent = "{}", DynamicEpoch = 3, DynamicRef = "eng-1:ctx:e000003", DynamicContentHash = "h" }),
            AssembleContext = new AssembleContextActivity(assembler),
        };
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        await pipeline.AssembleAsync(input, ResolvedModelFixture(), CancellationToken.None);
        Assert.Equal(new DynamicTierProvenance(input.EngagementId, 3, "eng-1:ctx:e000003"), assembler.ReceivedDynamicProvenance);
    }

    [Fact]
    public async Task AssembleAsync_ComposerReportsBaselineRelease_PackageCitesIt()
    {
        // S13.218 (doc 04 §4 step 2): the baseline tier cites the catalogue release and components the composer read.
        var d = Dependencies();
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);

        var package = await pipeline.AssembleAsync(input, ResolvedModelFixture(), CancellationToken.None);

        Assert.Equal(BaselineProvenanceFixture.BaselineVersion, package.Baseline.BaselineVersion);
        Assert.Equal(BaselineProvenanceFixture.Components, package.Baseline.Components);
    }

    private static readonly BaselineTierProvenance BaselineProvenanceFixture = new("firm-standards-v1", ["firm-standards", "playbooks"]);

    [Fact]
    public async Task AssembleAsync_RevisionNoteProvided_PassedToComposer()
    {
        var d = Dependencies();
        var composer = (FakeContextContentComposer)d.Composer;
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
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
        var d = Dependencies(scope) with { ToolCatalog = toolCatalog, Dispatcher = new AgentInvocationDispatcher(invoker, registry, TestContractValidator.Instance, new FakeResiliencePolicyProvider()), ContractTypes = registry };
        var pipeline = new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
        IReadOnlyList<McpToolPin> pins = [new McpToolPin { ToolRef = "com.example.crm/tickets/get_new_ticket", InputContract = RegistryContractFixtures.Ticket, Writes = false }];
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { ToolRefs = ["com.example.crm/tickets/get_new_ticket"], ToolPins = pins };
        var resolved = ResolvedModelFixture();
        var context = Package("{}");

        await pipeline.InvokeAgentAsync(input, resolved, context, "{}", CancellationToken.None);

        Assert.Equal(input.ToolRefs, toolCatalog.ReceivedToolRefs);
        Assert.Same(pins, toolCatalog.ReceivedPins);   // ADR-PA42: the catalogue builds each tool from its pin
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

        Assert.Equal(((ModelEntry)resolved.Entry).MaxOutputTokens, granted);
        Assert.NotNull(admission.ReceivedEstimate);
    }

    [Fact]
    public void EstimateCost_AgentEntry_IsTheFixedPerInvocationCost_AndGrantsNoTokensOrWindow()
    {
        // ADR-PA27, decision 1A: a remote agent reports no tokens the platform can price.
        Assert.Equal(0.0200m, AgentTaskActivityPipeline.EstimateCost(EchoAgent, promptTokens: 50_000, maxOutputTokens: 16_000));
        Assert.Equal(0, AgentTaskActivityPipeline.MaxOutputTokensOf(EchoAgent));
        Assert.Equal(0, AgentTaskActivityPipeline.ContextWindowOf(EchoAgent));
    }

    [Fact]
    public void ToSummary_AgentTarget_AttributesResourceVersionAndPinnedCardHash()
    {
        var resolved = ResolvedModelFixture() with { Provider = AgentEntry.A2aProvider, ModelId = EchoAgent.ResourceName, Entry = EchoAgent };

        var summary = AgentTaskActivityPipeline.ToSummary(resolved, "sha256:pinned-card", "resp_0139");

        Assert.Equal("com.azure.foundry/echo", summary.ResourceName);
        Assert.Equal("1.0", summary.ResourceVersion);
        Assert.Equal("sha256:pinned-card", summary.CardHash);
        Assert.Equal("resp_0139", summary.RemoteTaskId);   // ADR-PA27 addendum: the audit's join key to the agent's side
    }

    [Fact]
    public void ToSummary_ModelTarget_AddsNoAttribution_EvenIfACardHashOrTaskIdIsReported()
    {
        var summary = AgentTaskActivityPipeline.ToSummary(ResolvedModelFixture(), "sha256:ignored", "resp_ignored");

        Assert.Null(summary.ResourceName);
        Assert.Null(summary.ResourceVersion);
        Assert.Null(summary.CardHash);
        Assert.Null(summary.RemoteTaskId);
    }

    [Fact]
    public async Task InvokeAgentAsync_PassesTheResolvedEntryToTheInvokerAsTarget()
    {
        var pipeline = BuildPipeline(ScopeSectionFixture(), out var invoker, out _);
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture();

        await pipeline.InvokeAgentAsync(input, resolved, Package("{}"), "{}", CancellationToken.None);

        Assert.Same(resolved.Entry, invoker.ReceivedRequest!.Target);
    }

    /// <summary>ADR-PA40: an agent target with no ref invokes with no instructions and never asks the resolver.</summary>
    [Fact]
    public async Task InvokeAgentAsync_AgentTargetWithNoInstructionsRef_InvokesWithNoInstructionsAndNeverResolves()
    {
        var resolver = new FakeInstructionsResolver("instructions");
        var d = Dependencies(ScopeSectionFixture()) with { InstructionsResolver = resolver };
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { InstructionsRef = null };
        var resolved = ResolvedModelFixture() with { Provider = AgentEntry.A2aProvider, ModelId = EchoAgent.ResourceName, Entry = EchoAgent };

        await Pipeline(d).InvokeAgentAsync(input, resolved, Package("{}"), "{}", CancellationToken.None);

        Assert.False(resolver.WasCalled);
        Assert.Equal(string.Empty, d.Invoker.ReceivedRequest!.Instructions);
    }

    /// <summary>A ref a node does name is still resolved on an agent target (the A2A path then ignores it, S13.123(d)).</summary>
    [Fact]
    public async Task InvokeAgentAsync_AgentTargetWithAnInstructionsRef_StillResolvesIt()
    {
        var resolver = new FakeInstructionsResolver("instructions");
        var d = Dependencies(ScopeSectionFixture()) with { InstructionsResolver = resolver };
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null);
        var resolved = ResolvedModelFixture() with { Provider = AgentEntry.A2aProvider, ModelId = EchoAgent.ResourceName, Entry = EchoAgent };

        await Pipeline(d).InvokeAgentAsync(input, resolved, Package("{}"), "{}", CancellationToken.None);

        Assert.Equal("gen-scope.md", resolver.ReceivedInstructionsRef);
        Assert.Equal("instructions", d.Invoker.ReceivedRequest!.Instructions);
    }

    /// <summary>A model target with no ref fails permanently as a contract violation (invariant 7), before admission or invocation.</summary>
    [Fact]
    public async Task InvokeAgentAsync_ModelTargetWithNoInstructionsRef_FailsAsAContractViolation()
    {
        var resolver = new FakeInstructionsResolver("instructions");
        var d = Dependencies(ScopeSectionFixture()) with { InstructionsResolver = resolver };
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { InstructionsRef = null };

        var exception = await Assert.ThrowsAsync<ContractViolationException>(
            () => Pipeline(d).InvokeAgentAsync(input, ResolvedModelFixture(), Package("{}"), "{}", CancellationToken.None));

        Assert.Equal(nameof(AgentTaskActivityInput), exception.ContractType);
        Assert.Contains("gen-scope", Assert.Single(exception.Violations), StringComparison.Ordinal);
        Assert.Equal("contract_violation", GraphOrchestratorSteps.ClassifyFailure(exception));
        Assert.False(resolver.WasCalled);
        Assert.Null(d.AdmissionControllerDouble.ReceivedEstimate);
        Assert.Null(d.Invoker.ReceivedRequest);
    }

    private static readonly AgentEntry EchoAgent = new()
    {
        Provider = AgentEntry.A2aProvider,
        Currency = "USD",
        ResourceName = "com.azure.foundry/echo",
        ResourceVersion = "1.0",
        CostPerInvocation = 0.02m,
    };

    [Fact]
    public void Constructor_NullComposer_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(null!, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullAssembleContext_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, null!, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullContractTypes_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, null!, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullModelResolver_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, null!, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullAdmissionController_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, null!, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullInstructionsResolver_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, null!, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullToolCatalog_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, null!, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullDispatcher_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, null!, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullTelemetryStaging_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, null!, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance));
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
            InputCostPer1k = 0.003m,
            OutputCostPer1k = 0.015m,
            CacheReadCostPer1k = 0.0003m,
            Currency = "USD",
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

    /// <summary>ADR-E27 D8, D15: an input naming a registry contract is validated against its pinned snapshot, and the hashes reach the result and the audit telemetry.</summary>
    [Fact]
    public async Task RunAsync_RegistryContracts_ValidateAgainstThePinnedSnapshots_AndRecordTheirHashes()
    {
        var d = RegistryDependencies("""{"id":"T-2","priority":1}""");
        var pipeline = Pipeline(d);
        var input = BuildInput("acme/ticket/1.0", "acme/ticket/1.0", upstreamPayload: """{"id":"T-1","priority":3}""")
            with { InputContract = RegistryContractFixtures.Ticket, OutputContract = RegistryContractFixtures.Ticket };

        var result = await pipeline.RunAsync(input, CancellationToken.None);

        Assert.Equal("""{"id":"T-2","priority":1}""", result.OutputPayload);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, result.InputContractHash);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, result.OutputContractHash);
        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, record.InputContractHash);
        Assert.Equal(RegistryContractFixtures.Ticket.Hash, record.OutputContractHash);
    }

    /// <summary>S13.183 (ADR-E15 D3): the staged telemetry carries the same worker build as the result, so the signed audit can name it.</summary>
    [Fact]
    public async Task RunAsync_StagesTheWorkerBuildOnTheTelemetry()
    {
        var d = RegistryDependencies("""{"id":"T-2","priority":1}""");
        var input = BuildInput("acme/ticket/1.0", "acme/ticket/1.0", upstreamPayload: """{"id":"T-1","priority":3}""")
            with { InputContract = RegistryContractFixtures.Ticket, OutputContract = RegistryContractFixtures.Ticket };

        var result = await Pipeline(d).RunAsync(input, CancellationToken.None);

        var record = Assert.Single(d.TelemetryStaging.Records);
        Assert.Equal(TestHostBuildStamp.Value, record.HostBuild);
        Assert.Equal(result.HostBuild, record.HostBuild);
    }

    [Fact]
    public async Task RunAsync_AnInputThatFailsItsRegistryContract_IsRefusedBeforeTheAgentRuns()
    {
        var d = RegistryDependencies("""{"id":"T-2","priority":1}""");
        var input = BuildInput("acme/ticket/1.0", "acme/ticket/1.0", upstreamPayload: """{"id":"T-1"}""")
            with { InputContract = RegistryContractFixtures.Ticket, OutputContract = RegistryContractFixtures.Ticket };

        await Assert.ThrowsAsync<ContractViolationException>(() => Pipeline(d).RunAsync(input, CancellationToken.None));

        Assert.Null(d.Invoker.ReceivedRequest);
    }

    /// <summary>ADR-PA43: the input and the reply are validated under the run's pinned budgets, not the configured ones.</summary>
    [Theory]
    [InlineData("""{"id":"","priority":"x"}""", """{"id":"T-2","priority":1}""")]
    [InlineData("""{"id":"T-1","priority":3}""", """{"id":"","priority":"x"}""")]
    public async Task RunAsync_RegistryContracts_ValidateUnderThePinnedBudgets(string upstream, string reply)
    {
        var input = BuildInput("acme/ticket/1.0", "acme/ticket/1.0", upstreamPayload: upstream)
            with { InputContract = RegistryContractFixtures.Ticket, OutputContract = RegistryContractFixtures.Ticket };

        var configured = await Assert.ThrowsAsync<ContractViolationException>(() => Pipeline(RegistryDependencies(reply)).RunAsync(input, CancellationToken.None));
        var pinned = await Assert.ThrowsAsync<ContractViolationException>(() =>
            Pipeline(RegistryDependencies(reply)).RunAsync(input with { ContractBudgets = ContractBudgets.Default with { MaxViolations = 1 } }, CancellationToken.None));

        Assert.Equal(2, configured.Violations.Count);
        Assert.Single(pinned.Violations);
    }

    /// <summary>S13.217: the model call runs under the node's resilience profile, and under <c>llm-default</c> when it names none.</summary>
    [Theory]
    [InlineData("llm-interactive", "llm-interactive")]
    [InlineData(null, "llm-default")]
    public async Task RunAsync_RunsTheModelCallUnderTheNodesProfile(string? retryProfile, string expected)
    {
        var policy = new FakeResiliencePolicyProvider();
        var d = Dependencies(ScopeSectionFixture(), dynamicContent: """{"engagement_brief":"Design a product scope."}""");
        d = d with { Dispatcher = new AgentInvocationDispatcher(d.Invoker, d.ContractTypes, TestContractValidator.Instance, policy) };
        var input = BuildInput(nameof(BriefArtifact), nameof(SummaryArtifact), upstreamPayload: null) with { RetryProfile = retryProfile };

        await Pipeline(d).RunAsync(input, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(policy.RequestedEndpoints).Profile);
    }

    [Fact]
    public void Constructor_NullContractValidator_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), null!, TestHostBuildStamp.Instance));
    }

    [Fact]
    public void Constructor_NullHostBuild_Throws()
    {
        var d = Dependencies();

        Assert.Throws<ArgumentNullException>(() => new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, null!));
    }

    private static PipelineDependencies RegistryDependencies(string reply)
    {
        var d = Dependencies();
        var invoker = new FakeAgentInvoker(null, output: reply);
        return d with { Invoker = invoker, Dispatcher = new AgentInvocationDispatcher(invoker, d.ContractTypes, TestContractValidator.Instance, new FakeResiliencePolicyProvider()) };
    }

    private static AgentTaskActivityPipeline Pipeline(PipelineDependencies d) =>
        new(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);

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

        return new AgentTaskActivityPipeline(d.Composer, d.AssembleContext, d.ContractTypes, d.ModelResolver, d.AdmissionController, d.InstructionsResolver, d.ToolCatalog, d.Dispatcher, d.TelemetryStaging, new FakeEntryPayloadBuilder(), TestContractValidator.Instance, TestHostBuildStamp.Instance);
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
            Composer: new FakeContextContentComposer(new ComposedContext { BaselineContent = "{}", BaselineProvenance = BaselineProvenanceFixture, DynamicContent = dynamicContent, RealTimeContent = "{}" }),
            AssembleContext: new AssembleContextActivity(new FakeContextAssembler(Package(dynamicContent))),
            ContractTypes: registry,
            ModelResolver: new FakeModelResolver(ResolvedModelFixture()),
            AdmissionController: admission,
            InstructionsResolver: new FakeInstructionsResolver("instructions"),
            ToolCatalog: new FakeMcpToolCatalog(),
            Dispatcher: new AgentInvocationDispatcher(invoker, registry, TestContractValidator.Instance, new FakeResiliencePolicyProvider()),
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
