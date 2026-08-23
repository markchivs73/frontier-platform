using Frontier.Platform.Hitl;
using Frontier.Platform.Resilience;
using Microsoft.DurableTask;
using Frontier.Platform.Workflow.Model;

namespace Frontier.Platform.Workflow.Orchestration;

/// <summary>
/// The interpreter core (doc 00 §3.1, §4, ADR-1, ADR-2; S2.2). Pure: no
/// <c>DateTime.Now</c>/<c>UtcNow</c>, no GUIDs, no I/O — the pinned
/// <see cref="GraphOrchestratorInput.Definition"/> rides inline from orchestration
/// input and every non-deterministic concern (timestamps, activity calls, timers,
/// external events) goes through <paramref name="context"/>. The walk itself is
/// delegated to <see cref="GraphOrchestratorSteps"/> (dtf-determinism skill: keep the
/// orchestrator body free of evaluation logic). <see cref="IResiliencePolicyProvider"/>
/// is constructor-injected (doc 10 §5 outer loop): it is a pure function of a profile
/// name over the compiled-in <see cref="Phase1ResilienceProfileCatalogue"/>, so it
/// returns identical <see cref="TaskOptions"/> on every replay (dtf-determinism).
/// <see cref="IMcpWriteClassifier"/> is constructor-injected on the same terms (S13.12h): the
/// write/read classification is deployment knowledge, and the port's contract requires a pure,
/// replay-stable answer.
/// <see cref="IRollbackPlanner"/> is similarly constructor-injected (doc 06 §9): it is a
/// pure function over <see cref="GraphExecutionState.ApprovedSnapshotRefs"/> and the
/// cascade's downstream set, used by <see cref="GraphOrchestratorSteps.RunGateAsync"/> on
/// a gate rejection.
/// </summary>
[DurableTask(WorkflowActivityNames.GraphOrchestrator)]
public sealed class GraphOrchestrator(IResiliencePolicyProvider policyProvider, IRollbackPlanner rollbackPlanner, IMcpWriteClassifier mcpWriteClassifier) : TaskOrchestrator<GraphOrchestratorInput, GraphOrchestratorResult>
{
    /// <inheritdoc />
    public override async Task<GraphOrchestratorResult> RunAsync(TaskOrchestrationContext context, GraphOrchestratorInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var startedAtUtc = context.CurrentUtcDateTime;

        var state = await GraphOrchestratorSteps.RunInitialWalkAsync(context, input, rollbackPlanner, policyProvider, mcpWriteClassifier);
        await GraphOrchestratorSteps.RunCascadeWalkAsync(context, input, state, policyProvider, mcpWriteClassifier);
        await GraphOrchestratorSteps.WriteFinalSnapshotAsync(context, input, state, policyProvider);
        await GraphOrchestratorSteps.ConsolidateAuditAsync(context, input, startedAtUtc, policyProvider);

        return new GraphOrchestratorResult
        {
            CompletedSteps = state.CompletedSteps,
            ArtifactStatuses = state.ArtifactStatuses,
        };
    }
}
