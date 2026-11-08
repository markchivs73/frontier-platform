using Frontier.Platform.Abstractions;
using Frontier.Platform.Hitl;
using Frontier.Platform.Resilience;
using Frontier.Platform.Workflow.Model;
using Microsoft.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Frontier.Platform.Workflow.Orchestration.Tests;

/// <summary>
/// S13.247 (doc 10 §3–§4, invariant 7): the engine activities that ran with no retry now each run
/// under a named profile, resolved by the real <see cref="IResiliencePolicyProvider"/>. A store-backed
/// activity re-runs on a passing fault and fails at once on a contract violation; the pure cascade
/// evaluation runs once whatever the fault, because a re-run would fail the same way.
/// <see cref="FakeTaskOrchestrationContext"/> applies the <see cref="RetryPolicy"/> the step passes the
/// way the SDK does, so each test observes how many times the activity ran.
/// </summary>
public sealed class EngineActivityRetryTests : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection().AddFrontierResilience().BuildServiceProvider();
    private readonly FakeTaskOrchestrationContext context = new();
    private readonly RecordingPolicyProvider policies;

    /// <summary>Builds the recording provider over the real Resilience registrations.</summary>
    public EngineActivityRetryTests() =>
        policies = new RecordingPolicyProvider(services.GetRequiredService<IResiliencePolicyProvider>());

    /// <inheritdoc />
    public void Dispose() => services.Dispose();

    /// <summary>The store-backed activities, each driven through the step that calls it.</summary>
    public static TheoryData<string> StoreActivities() => new(
        WorkflowActivityNames.ArtifactStateActivity,
        WorkflowActivityNames.RestoreArtifactsActivity,
        WorkflowActivityNames.RequestApprovalActivity,
        WorkflowActivityNames.EscalateApprovalActivity,
        WorkflowActivityNames.ResolveDispatcherVersionActivity);

    /// <summary>A passing fault on the first attempt is retried: the activity runs twice and the step asked for <c>storage</c>.</summary>
    [Theory]
    [MemberData(nameof(StoreActivities))]
    public async Task StoreActivity_TransientFault_IsRetriedUnderTheStorageProfile(string activity)
    {
        Register(activity, failures: 1, new TimeoutException("store unavailable"));

        await CallAsync(activity);

        Assert.Equal(2, context.ActivityAttempts[activity]);
        Assert.Equal([GraphOrchestratorSteps.StorageProfile], policies.RequestedProfileNames);
    }

    /// <summary>A contract violation is permanent (invariant 7): the activity runs once and the violation surfaces.</summary>
    [Theory]
    [MemberData(nameof(StoreActivities))]
    public async Task StoreActivity_ContractViolation_IsNotRetried(string activity)
    {
        Register(activity, failures: int.MaxValue, new ContractViolationException("ApprovalRequest", ["bad"]));

        await Assert.ThrowsAsync<ContractViolationException>(() => CallAsync(activity));

        Assert.Equal(1, context.ActivityAttempts[activity]);
    }

    /// <summary>A fault on every attempt fails the step after the profile's outer attempts, no more.</summary>
    [Theory]
    [MemberData(nameof(StoreActivities))]
    public async Task StoreActivity_FaultThatOutlastsTheProfile_FailsAfterItsOuterAttempts(string activity)
    {
        Register(activity, failures: int.MaxValue, new TimeoutException("store down"));

        await Assert.ThrowsAsync<TimeoutException>(() => CallAsync(activity));

        Assert.Equal(Phase1ResilienceProfileCatalogue.Storage.OuterRetry.MaxAttempts, context.ActivityAttempts[activity]);
    }

    /// <summary>Cascade evaluation is pure, so it runs under <c>none</c> and a fault is not retried.</summary>
    [Fact]
    public async Task CascadeEvaluation_AnyFault_RunsOnceUnderTheNoneProfile()
    {
        Register(WorkflowActivityNames.EvaluateCascadeActivity, failures: 1, new TimeoutException("worker hiccup"));

        await Assert.ThrowsAsync<TimeoutException>(() => CallAsync(WorkflowActivityNames.EvaluateCascadeActivity));

        Assert.Equal(1, context.ActivityAttempts[WorkflowActivityNames.EvaluateCascadeActivity]);
        Assert.Equal([GraphOrchestratorSteps.PureActivityProfile], policies.RequestedProfileNames);
    }

    /// <summary>A contract violation from cascade evaluation runs once and surfaces.</summary>
    [Fact]
    public async Task CascadeEvaluation_ContractViolation_IsNotRetried()
    {
        Register(WorkflowActivityNames.EvaluateCascadeActivity, failures: int.MaxValue, new ContractViolationException("CascadeActivityRequest", ["bad"]));

        await Assert.ThrowsAsync<ContractViolationException>(() => CallAsync(WorkflowActivityNames.EvaluateCascadeActivity));

        Assert.Equal(1, context.ActivityAttempts[WorkflowActivityNames.EvaluateCascadeActivity]);
    }

    /// <summary>A cascade evaluation that succeeds runs once.</summary>
    [Fact]
    public async Task CascadeEvaluation_Success_ReturnsTheActivityResponse()
    {
        Register(WorkflowActivityNames.EvaluateCascadeActivity, failures: 0, new TimeoutException("unused"));

        await CallAsync(WorkflowActivityNames.EvaluateCascadeActivity);

        Assert.Equal(1, context.ActivityAttempts[WorkflowActivityNames.EvaluateCascadeActivity]);
    }

    /// <summary>Registers <paramref name="activity"/> to throw <paramref name="fault"/> on its first <paramref name="failures"/> attempts, then succeed.</summary>
    private void Register(string activity, int failures, Exception fault)
    {
        var success = Success(activity);
        var attempts = 0;
        context.ActivityHandlers[activity] = input => ++attempts <= failures ? throw fault : success(input);
    }

    /// <summary>The successful response <paramref name="activity"/> returns for its input.</summary>
    private static Func<object?, object> Success(string activity) => activity switch
    {
        WorkflowActivityNames.ArtifactStateActivity or WorkflowActivityNames.RestoreArtifactsActivity => _ => new ArtifactStateActivityResponse { SectionRef = "scope-ref-v1" },
        WorkflowActivityNames.RequestApprovalActivity or WorkflowActivityNames.EscalateApprovalActivity => _ => Approval(),
        WorkflowActivityNames.ResolveDispatcherVersionActivity => _ => new ResolveDispatcherVersionResult(),
        _ => _ => new CascadeActivityResponse { ChangedArtifact = "scope", DownstreamArtifacts = [], SkippedArtifacts = [] },
    };

    /// <summary>Drives the orchestrator step that calls <paramref name="activity"/>.</summary>
    private Task CallAsync(string activity)
    {
        var definition = OrchestrationFixtures.ChainWithBusinessGate();
        var input = OrchestrationFixtures.Input(definition);
        var state = new GraphExecutionState { StartedAtUtc = OrchestrationFixtures.StartedAtUtc };
        var gate = (HumanGateNode)definition.Nodes[3];

        return activity switch
        {
            WorkflowActivityNames.ArtifactStateActivity => GraphOrchestratorSteps.WriteArtifactVersionAsync(context, input, state, "scope", "content", "hash", policies),
            WorkflowActivityNames.RestoreArtifactsActivity => GraphOrchestratorSteps.RestoreArtifactsAsync(context, input, new Dictionary<string, string> { ["scope"] = "scope-ref-v1" }, state, policies),
            WorkflowActivityNames.RequestApprovalActivity => GraphOrchestratorSteps.OpenGateAsync(context, input, gate, state, policies),
            WorkflowActivityNames.EscalateApprovalActivity => GraphOrchestratorSteps.EscalateAsync(context, Approval(), policies),
            WorkflowActivityNames.ResolveDispatcherVersionActivity => DispatcherOrchestrator.RollOverAsync(context, DispatcherFixtures.Input(), policies),
            _ => GraphOrchestratorSteps.EvaluateCascadeAsync(context, definition, state, "scope", policies),
        };
    }

    /// <summary>A pending business-gate approval the approval steps open or escalate.</summary>
    private static ApprovalRequest Approval() => new()
    {
        Id = "eng-1::wf-chain:gate-business-1:0",
        EngagementId = "eng-1",
        ExecutionId = "eng-1::wf-chain",
        GateId = "gate-business-1",
        GateKind = GateKind.Business,
        ApproverRoles = ["approver"],
        SectionRefs = new Dictionary<string, string>(),
        Status = ApprovalRequestStatus.Pending,
        RequestedAtUtc = OrchestrationFixtures.StartedAtUtc,
    };

    /// <summary>The real provider, recording every profile an orchestrator step asks it for.</summary>
    private sealed class RecordingPolicyProvider(IResiliencePolicyProvider inner) : IResiliencePolicyProvider
    {
        /// <summary>Every profile name asked for, in order.</summary>
        public List<string> RequestedProfileNames { get; } = [];

        /// <inheritdoc />
        public ResiliencePipeline GetPipeline(string profileName) => inner.GetPipeline(profileName);

        /// <inheritdoc />
        public TaskOptions GetTaskOptions(string profileName)
        {
            RequestedProfileNames.Add(profileName);
            return inner.GetTaskOptions(profileName);
        }
    }
}
